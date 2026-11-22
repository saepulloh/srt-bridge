using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GDI.Core.Models;
using GDI.Core.Services;
using GDI.Multiview;

namespace GDI.Desktop.ViewModels;

public sealed class ChannelOptionItem
{
    public Guid? Id { get; }
    public string DisplayName { get; }

    public ChannelOptionItem(Guid? id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj)
    {
        if (obj is ChannelOptionItem other)
        {
            return Id == other.Id;
        }
        return false;
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}

public sealed class CustomTileMappingItem : ViewModelBase
{
    public int TileIndex { get; set; }
    public string TileLabel => $"TILE {TileIndex + 1:D2}";
    public ObservableCollection<ChannelOptionItem> AvailableChannels { get; } = [];

    private ChannelOptionItem? _selectedChannel;
    public ChannelOptionItem? SelectedChannel
    {
        get => _selectedChannel;
        set => SetProperty(ref _selectedChannel, value);
    }
}

public sealed class MultiviewTileItem : ViewModelBase
{
    private MultiviewTile _tile = new();
    private MainViewModel? _main;
    private bool _suppressSelectionCallback;

    public int TileIndex => _tile.TileIndex;
    public int TileNumber => _tile.TileIndex + 1;
    public Guid? AssignedChannelId => _tile.AssignedChannelId;
    public string ChannelName => _tile.ChannelName;
    public string TallyState => _tile.TallyState;
    public string ResolutionFps => _tile.ResolutionFps;
    public double BitrateKbps => _tile.BitrateKbps;
    public double SrtRttMs => _tile.SrtRttMs;
    public bool IsLive => _tile.IsLive;
    public float AudioLevelLeftDb => _tile.AudioLevelLeftDb;
    public float AudioLevelRightDb => _tile.AudioLevelRightDb;

    public ObservableCollection<ChannelOptionItem> AvailableChannels { get; } = [];

    private ChannelOptionItem? _selectedChannel;
    public ChannelOptionItem? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (SetProperty(ref _selectedChannel, value) && !_suppressSelectionCallback && _main != null)
            {
                _main.MultiviewEngine.AssignChannelToTile(TileIndex, value?.Id);
                _main.MultiviewEngine.SaveConfig();
                _main.MultiviewVM.RefreshTiles();
            }
        }
    }

    private double _vuLevelLeft;
    public double VuLevelLeft
    {
        get => _vuLevelLeft;
        set => SetProperty(ref _vuLevelLeft, value);
    }

    private double _vuLevelRight;
    public double VuLevelRight
    {
        get => _vuLevelRight;
        set => SetProperty(ref _vuLevelRight, value);
    }

    private double _vuPeakHoldLeft;
    public double VuPeakHoldLeft
    {
        get => _vuPeakHoldLeft;
        set => SetProperty(ref _vuPeakHoldLeft, value);
    }

    private double _vuPeakHoldRight;
    public double VuPeakHoldRight
    {
        get => _vuPeakHoldRight;
        set => SetProperty(ref _vuPeakHoldRight, value);
    }

    private bool _isClipping;
    public bool IsClipping
    {
        get => _isClipping;
        set => SetProperty(ref _isClipping, value);
    }

    private string _audioLevelText = "OFF";
    public string AudioLevelText
    {
        get => _audioLevelText;
        set => SetProperty(ref _audioLevelText, value);
    }

    private string _telemetryHudText = "";
    public string TelemetryHudText
    {
        get => _telemetryHudText;
        set => SetProperty(ref _telemetryHudText, value);
    }

    private string _activeBackendText = "";
    public string ActiveBackendText
    {
        get => _activeBackendText;
        set => SetProperty(ref _activeBackendText, value);
    }

    private ImageSource? _frameSource;
    public ImageSource? FrameSource
    {
        get => _frameSource;
        set => SetProperty(ref _frameSource, value);
    }

    private bool _hasFrame;
    public bool HasFrame
    {
        get => _hasFrame;
        set => SetProperty(ref _hasFrame, value);
    }

    private WriteableBitmap? _writeableBitmap;
    private int _isRenderingFrame = 0;
    private long _lastFrameRenderTicks = 0;
    private long _lastPreviewRefreshTick = 0;

    public MultiviewTileItem()
    {
        RealtimeFrameDistributor.Instance.OnFrameAvailable += HandleRealtimeFrame;
    }

    private void HandleRealtimeFrame(Guid channelId, RealtimeVideoFrame frame)
    {
        if (!IsLive || AssignedChannelId != channelId) return;

        // Cap preview rendering to 30 FPS to prevent Dispatcher saturation on high frame-rate feeds (50/60p)
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long minIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 30;
        if (nowTicks - _lastFrameRenderTicks < minIntervalTicks) return;

        // Skip frame if previous frame is still rendering on UI thread (prevents message queue backlog)
        if (Interlocked.CompareExchange(ref _isRenderingFrame, 1, 0) != 0) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            Interlocked.Exchange(ref _isRenderingFrame, 0);
            return;
        }

        dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            try
            {
                _lastFrameRenderTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                int w = frame.Width;
                int h = frame.Height;
                if (w <= 0 || h <= 0) return;

                if (_writeableBitmap == null || _writeableBitmap.PixelWidth != w || _writeableBitmap.PixelHeight != h)
                {
                    _writeableBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                    FrameSource = _writeableBitmap;
                }

                _writeableBitmap.Lock();
                IntPtr backBuf = _writeableBitmap.BackBuffer;

                bool isUyvy = string.Equals(frame.PixelFormat, "UYVY", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(frame.PixelFormat, "uyvy422", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(frame.PixelFormat, "2vuy", StringComparison.OrdinalIgnoreCase);

                if (isUyvy)
                {
                    unsafe
                    {
                        fixed (byte* pSrcBase = frame.Data)
                        {
                            byte* pSrc = pSrcBase;
                            byte* pDst = (byte*)backBuf.ToPointer();
                            int pixels = w * h;
                            int pairs = Math.Min(pixels / 2, frame.Data.Length / 4);

                            for (int i = 0; i < pairs; i++)
                            {
                                int u = pSrc[0] - 128;
                                int y0 = pSrc[1] - 16;
                                int v = pSrc[2] - 128;
                                int y1 = pSrc[3] - 16;
                                pSrc += 4;

                                int r0 = Math.Clamp((298 * y0 + 409 * v + 128) >> 8, 0, 255);
                                int g0 = Math.Clamp((298 * y0 - 100 * u - 208 * v + 128) >> 8, 0, 255);
                                int b0 = Math.Clamp((298 * y0 + 516 * u + 128) >> 8, 0, 255);

                                pDst[0] = (byte)b0;
                                pDst[1] = (byte)g0;
                                pDst[2] = (byte)r0;
                                pDst[3] = 255;
                                pDst += 4;

                                int r1 = Math.Clamp((298 * y1 + 409 * v + 128) >> 8, 0, 255);
                                int g1 = Math.Clamp((298 * y1 - 100 * u - 208 * v + 128) >> 8, 0, 255);
                                int b1 = Math.Clamp((298 * y1 + 516 * u + 128) >> 8, 0, 255);

                                pDst[0] = (byte)b1;
                                pDst[1] = (byte)g1;
                                pDst[2] = (byte)r1;
                                pDst[3] = 255;
                                pDst += 4;
                            }
                        }
                    }
                }
                else
                {
                    System.Runtime.InteropServices.Marshal.Copy(frame.Data, 0, backBuf, Math.Min(frame.Data.Length, w * h * 4));
                }

                _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
                _writeableBitmap.Unlock();
                HasFrame = true;
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isRenderingFrame, 0);
            }
        });
    }

    public void SetContext(MainViewModel main)
    {
        _main = main;
    }

    public void Update(MultiviewTile tile, IReadOnlyList<ChannelConfig> channels)
    {
        _tile = tile;
        OnPropertyChanged(nameof(TileIndex));
        OnPropertyChanged(nameof(TileNumber));
        OnPropertyChanged(nameof(AssignedChannelId));
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(TallyState));
        OnPropertyChanged(nameof(ResolutionFps));
        OnPropertyChanged(nameof(BitrateKbps));
        OnPropertyChanged(nameof(SrtRttMs));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(AudioLevelLeftDb));
        OnPropertyChanged(nameof(AudioLevelRightDb));

        TelemetryHudText = $"{tile.ResolutionFps} · {tile.BitrateKbps:F0} kbps · Prev: {tile.PreviewFps:F0} fps";
        ActiveBackendText = !string.IsNullOrEmpty(tile.ActiveHardwareBackend) ? tile.ActiveHardwareBackend : "Hardware DMA Direct";

        // Only rebuild AvailableChannels collection if channel count or IDs have actually changed
        bool needsRebuild = AvailableChannels.Count != (channels.Count + 1);
        if (!needsRebuild)
        {
            for (int i = 0; i < channels.Count; i++)
            {
                if (AvailableChannels[i + 1].Id != channels[i].Id)
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            _suppressSelectionCallback = true;
            try
            {
                AvailableChannels.Clear();
                AvailableChannels.Add(new ChannelOptionItem(null, "Auto (Slot Order)"));
                foreach (var c in channels)
                {
                    var dirLabel = c.Direction == ChannelDirection.Encoder ? "ENC" : "DEC";
                    AvailableChannels.Add(new ChannelOptionItem(c.Id, $"{c.Name} [{dirLabel}]"));
                }
            }
            finally
            {
                _suppressSelectionCallback = false;
            }
        }

        var currentAssigned = _main?.MultiviewEngine.GetAssignedChannel(TileIndex);
        if (SelectedChannel?.Id != currentAssigned)
        {
            _suppressSelectionCallback = true;
            try
            {
                SelectedChannel = AvailableChannels.FirstOrDefault(o => o.Id == currentAssigned) ?? AvailableChannels[0];
            }
            finally
            {
                _suppressSelectionCallback = false;
            }
        }

        // Calculate Responsive Audio VU Meters from True Decoded PCM Metrics
        if (IsLive)
        {
            float leftDb = tile.AudioLevelLeftDb;
            float rightDb = tile.AudioLevelRightDb;
            float holdL = tile.AudioPeakHoldLeftDb;
            float holdR = tile.AudioPeakHoldRightDb;

            VuLevelLeft = Math.Clamp((leftDb + 60f) / 60f * 100.0, 0, 100);
            VuLevelRight = Math.Clamp((rightDb + 60f) / 60f * 100.0, 0, 100);
            VuPeakHoldLeft = Math.Clamp((holdL + 60f) / 60f * 100.0, 0, 100);
            VuPeakHoldRight = Math.Clamp((holdR + 60f) / 60f * 100.0, 0, 100);
            IsClipping = tile.IsAudioClippingLeft || tile.IsAudioClippingRight;
            AudioLevelText = tile.IsAudioSilent ? "SILENCE" : $"{Math.Max(leftDb, rightDb):F1} dBFS";
        }
        else
        {
            VuLevelLeft = 0;
            VuLevelRight = 0;
            VuPeakHoldLeft = 0;
            VuPeakHoldRight = 0;
            IsClipping = false;
            AudioLevelText = "OFF";
        }

        if (!HasFrame)
        {
            RefreshFrame();
        }
    }

    public void RefreshFrame()
    {
        if (IsLive && !string.IsNullOrWhiteSpace(_tile.PreviewImagePath) && File.Exists(_tile.PreviewImagePath))
        {
            try
            {
                using var fs = new FileStream(_tile.PreviewImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;

                if (ms.Length > 0)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    FrameSource = bmp;
                    HasFrame = true;
                    return;
                }
            }
            catch { }
        }

        if (!IsLive)
        {
            FrameSource = null;
            HasFrame = false;
            _writeableBitmap = null;
        }
    }

    public void TickRealtime()
    {
        if (IsLive)
        {
            if (!HasFrame && (Environment.TickCount64 - _lastPreviewRefreshTick > 1000))
            {
                _lastPreviewRefreshTick = Environment.TickCount64;
                RefreshFrame();
            }

            if (AssignedChannelId.HasValue && _main != null)
            {
                var m = _main.ChannelManager.GetMetrics(AssignedChannelId.Value);
                _tile.AudioLevelLeftDb = m.AudioLevelLeftDb;
                _tile.AudioLevelRightDb = m.AudioLevelRightDb;
                _tile.AudioPeakHoldLeftDb = m.AudioPeakHoldLeftDb;
                _tile.AudioPeakHoldRightDb = m.AudioPeakHoldRightDb;
                _tile.IsAudioClippingLeft = m.IsAudioClippingLeft;
                _tile.IsAudioClippingRight = m.IsAudioClippingRight;
                _tile.IsAudioSilent = m.IsAudioSilent;
            }

            float leftDb = _tile.AudioLevelLeftDb;
            float rightDb = _tile.AudioLevelRightDb;

            double targetL = Math.Clamp((leftDb + 60f) / 60f * 100.0, 0, 100);
            double targetR = Math.Clamp((rightDb + 60f) / 60f * 100.0, 0, 100);

            // Fast attack, smooth decay
            VuLevelLeft = targetL >= VuLevelLeft ? targetL : Math.Max(targetL, VuLevelLeft * 0.90);
            VuLevelRight = targetR >= VuLevelRight ? targetR : Math.Max(targetR, VuLevelRight * 0.90);
            VuPeakHoldLeft = Math.Clamp((_tile.AudioPeakHoldLeftDb + 60f) / 60f * 100.0, 0, 100);
            VuPeakHoldRight = Math.Clamp((_tile.AudioPeakHoldRightDb + 60f) / 60f * 100.0, 0, 100);
            IsClipping = _tile.IsAudioClippingLeft || _tile.IsAudioClippingRight;
            AudioLevelText = _tile.IsAudioSilent ? "SILENCE" : $"{Math.Max(leftDb, rightDb):F1} dBFS";
        }
        else
        {
            if (VuLevelLeft > 0) VuLevelLeft = Math.Max(0, VuLevelLeft * 0.85);
            if (VuLevelRight > 0) VuLevelRight = Math.Max(0, VuLevelRight * 0.85);
            VuPeakHoldLeft = 0;
            VuPeakHoldRight = 0;
            IsClipping = false;
            AudioLevelText = "OFF";
            if (HasFrame)
            {
                FrameSource = null;
                HasFrame = false;
                _writeableBitmap = null;
            }
        }
    }
}

public sealed class MultiviewViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly System.Windows.Threading.DispatcherTimer _realtimeTimer;

    private MultiviewLayout _currentLayout = MultiviewLayout.Quad;
    public MultiviewLayout CurrentLayout
    {
        get => _currentLayout;
        set
        {
            if (SetProperty(ref _currentLayout, value))
            {
                _main.MultiviewEngine.CurrentLayout = value;
                _main.MultiviewEngine.SaveConfig();
                OnPropertyChanged(nameof(GridRows));
                OnPropertyChanged(nameof(GridColumns));
                RefreshTiles();
            }
        }
    }

    public int GridRows => CurrentLayout switch
    {
        MultiviewLayout.Single => 1,
        MultiviewLayout.Dual => 1,
        MultiviewLayout.Quad => 2,
        MultiviewLayout.Hexa => 2,
        MultiviewLayout.Octo => 2,
        MultiviewLayout.Nine => 3,
        MultiviewLayout.Sixteen => 4,
        MultiviewLayout.Custom => _main.MultiviewEngine.CustomRows,
        _ => 2
    };

    public int GridColumns => CurrentLayout switch
    {
        MultiviewLayout.Single => 1,
        MultiviewLayout.Dual => 2,
        MultiviewLayout.Quad => 2,
        MultiviewLayout.Hexa => 3,
        MultiviewLayout.Octo => 4,
        MultiviewLayout.Nine => 3,
        MultiviewLayout.Sixteen => 4,
        MultiviewLayout.Custom => _main.MultiviewEngine.CustomColumns,
        _ => 2
    };

    public ObservableCollection<MultiviewTileItem> Tiles { get; } = [];

    // Custom Multiview Dialog Properties
    private bool _isCustomConfigOpen;
    public bool IsCustomConfigOpen
    {
        get => _isCustomConfigOpen;
        set => SetProperty(ref _isCustomConfigOpen, value);
    }

    private int _customRows = 2;
    public int CustomRows
    {
        get => _customRows;
        set
        {
            if (SetProperty(ref _customRows, value))
            {
                OnPropertyChanged(nameof(TotalCustomTilesText));
                PopulateCustomMappings();
            }
        }
    }

    private int _customColumns = 3;
    public int CustomColumns
    {
        get => _customColumns;
        set
        {
            if (SetProperty(ref _customColumns, value))
            {
                OnPropertyChanged(nameof(TotalCustomTilesText));
                PopulateCustomMappings();
            }
        }
    }

    public string TotalCustomTilesText => $"{CustomRows} Rows × {CustomColumns} Columns = {CustomRows * CustomColumns} Tiles";

    public ObservableCollection<CustomTileMappingItem> CustomTileMappings { get; } = [];

    public RelayCommand SetLayoutCommand { get; }
    public RelayCommand ToggleFullscreenCommand { get; }
    public RelayCommand OpenCustomConfigCommand { get; }
    public RelayCommand CloseCustomConfigCommand { get; }
    public RelayCommand ApplyCustomConfigCommand { get; }
    public RelayCommand ResetToAutoCommand { get; }

    public MultiviewViewModel(MainViewModel main)
    {
        _main = main;
        _currentLayout = _main.MultiviewEngine.CurrentLayout;
        _customRows = _main.MultiviewEngine.CustomRows;
        _customColumns = _main.MultiviewEngine.CustomColumns;

        _realtimeTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40) // 25 FPS smooth real-time
        };
        _realtimeTimer.Tick += (_, _) =>
        {
            for (int i = 0; i < Tiles.Count; i++)
            {
                Tiles[i].TickRealtime();
            }
        };
        _realtimeTimer.Start();

        SetLayoutCommand = new RelayCommand(param =>
        {
            if (param is string layoutStr && Enum.TryParse<MultiviewLayout>(layoutStr, true, out var layout))
            {
                CurrentLayout = layout;
            }
        });

        ToggleFullscreenCommand = new RelayCommand(() =>
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow mw)
            {
                mw.ToggleFullscreen();
            }
            else
            {
                _main.LogsVM.Log("Toggled Multiview Fullscreen mode (F11).", "Multiview");
            }
        });

        OpenCustomConfigCommand = new RelayCommand(() =>
        {
            CustomRows = _main.MultiviewEngine.CustomRows;
            CustomColumns = _main.MultiviewEngine.CustomColumns;
            PopulateCustomMappings();
            IsCustomConfigOpen = true;
        });

        CloseCustomConfigCommand = new RelayCommand(() =>
        {
            IsCustomConfigOpen = false;
        });

        ApplyCustomConfigCommand = new RelayCommand(() =>
        {
            _main.MultiviewEngine.CustomRows = CustomRows;
            _main.MultiviewEngine.CustomColumns = CustomColumns;
            foreach (var item in CustomTileMappings)
            {
                _main.MultiviewEngine.AssignChannelToTile(item.TileIndex, item.SelectedChannel?.Id);
            }
            _main.MultiviewEngine.CurrentLayout = MultiviewLayout.Custom;
            _main.MultiviewEngine.SaveConfig();

            CurrentLayout = MultiviewLayout.Custom;
            OnPropertyChanged(nameof(GridRows));
            OnPropertyChanged(nameof(GridColumns));
            RefreshTiles();
            IsCustomConfigOpen = false;
            _main.LogsVM.Log($"Applied Custom Multiview layout ({CustomRows}x{CustomColumns}).", "Multiview");
        });

        ResetToAutoCommand = new RelayCommand(() =>
        {
            _main.MultiviewEngine.ClearAssignments();
            _main.MultiviewEngine.SaveConfig();
            PopulateCustomMappings();
            RefreshTiles();
            _main.LogsVM.Log("Reset Multiview tile assignments to automatic.", "Multiview");
        });
    }

    private void PopulateCustomMappings()
    {
        var channels = _main.ChannelManager.GetAll();
        var totalTiles = Math.Clamp(CustomRows * CustomColumns, 1, 36);

        CustomTileMappings.Clear();
        for (int i = 0; i < totalTiles; i++)
        {
            var item = new CustomTileMappingItem { TileIndex = i };
            item.AvailableChannels.Add(new ChannelOptionItem(null, "Auto (Slot Order)"));
            foreach (var c in channels)
            {
                var dirLabel = c.Direction == ChannelDirection.Encoder ? "ENC" : "DEC";
                item.AvailableChannels.Add(new ChannelOptionItem(c.Id, $"{c.Name} [{dirLabel}]"));
            }

            var assigned = _main.MultiviewEngine.GetAssignedChannel(i);
            item.SelectedChannel = item.AvailableChannels.FirstOrDefault(o => o.Id == assigned) ?? item.AvailableChannels[0];
            CustomTileMappings.Add(item);
        }
    }

    public void RefreshTiles()
    {
        var channels = _main.ChannelManager.GetAll();
        var rawTiles = _main.MultiviewEngine.GetTiles(channels, id => _main.ChannelManager.GetMetrics(id));

        Tiles.Clear();
        foreach (var t in rawTiles)
        {
            var item = new MultiviewTileItem();
            item.SetContext(_main);
            item.Update(t, channels);
            Tiles.Add(item);
        }
    }

    public void UpdateTelemetry(IReadOnlyList<ChannelConfig> channels)
    {
        var rawTiles = _main.MultiviewEngine.GetTiles(channels, id => _main.ChannelManager.GetMetrics(id));
        while (Tiles.Count < rawTiles.Count)
        {
            var item = new MultiviewTileItem();
            item.SetContext(_main);
            Tiles.Add(item);
        }
        while (Tiles.Count > rawTiles.Count)
        {
            Tiles.RemoveAt(Tiles.Count - 1);
        }

        for (int i = 0; i < rawTiles.Count; i++)
        {
            Tiles[i].SetContext(_main);
            Tiles[i].Update(rawTiles[i], channels);
        }
    }
}
