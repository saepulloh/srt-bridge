using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GDI.Core.Models;

namespace GDI.Desktop.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    // System Overview Glass Card Metrics
    private double _cpuPercent;
    public double CpuPercent { get => _cpuPercent; set => SetProperty(ref _cpuPercent, value); }

    private string _cpuDetails = "Intel / AMD CPU";
    public string CpuDetails { get => _cpuDetails; set => SetProperty(ref _cpuDetails, value); }

    private double _ramPercent;
    public double RamPercent { get => _ramPercent; set => SetProperty(ref _ramPercent, value); }

    private string _ramDetails = "0 / 0 GiB";
    public string RamDetails { get => _ramDetails; set => SetProperty(ref _ramDetails, value); }

    private string _gpuStatus = "NVIDIA NVENC / Intel QSV Ready";
    public string GpuStatus { get => _gpuStatus; set => SetProperty(ref _gpuStatus, value); }

    private string _gpuDetails = "VRAM: 0 / 0 GiB";
    public string GpuDetails { get => _gpuDetails; set => SetProperty(ref _gpuDetails, value); }

    private string _networkThroughput = "TX: 0.0 Mbps · RX: 0.0 Mbps";
    public string NetworkThroughput { get => _networkThroughput; set => SetProperty(ref _networkThroughput, value); }

    // Streaming Overview Glass Card
    private int _totalChannels;
    public int TotalChannels { get => _totalChannels; set => SetProperty(ref _totalChannels, value); }

    private int _activeEncoders;
    public int ActiveEncoders { get => _activeEncoders; set => SetProperty(ref _activeEncoders, value); }

    private int _activeDecoders;
    public int ActiveDecoders { get => _activeDecoders; set => SetProperty(ref _activeDecoders, value); }

    private string _totalBitrateText = "0 kbps";
    public string TotalBitrateText { get => _totalBitrateText; set => SetProperty(ref _totalBitrateText, value); }

    // Hardware Overview Glass Card
    private string _bmdDeckLinkStatus = "Blackmagic DeckLink Duo 2 (4x 3G-SDI Ready)";
    public string BmdDeckLinkStatus { get => _bmdDeckLinkStatus; set => SetProperty(ref _bmdDeckLinkStatus, value); }

    private string _ajaStatus = "AJA NTV2 (Not Configured / No Hardware)";
    public string AjaStatus { get => _ajaStatus; set => SetProperty(ref _ajaStatus, value); }

    private string _magewellStatus = "Magewell MWCapture (Not Configured / No Hardware)";
    public string MagewellStatus { get => _magewellStatus; set => SetProperty(ref _magewellStatus, value); }

    // Channel Cards
    public ObservableCollection<ChannelCardViewModel> Channels { get; } = [];

    // Commands
    public AsyncRelayCommand AddChannelCommand { get; }
    public AsyncRelayCommand RefreshChannelsCommand { get; }

    public DashboardViewModel(MainViewModel main)
    {
        _main = main;
        AddChannelCommand = new AsyncRelayCommand(() => _main.OpenChannelModalAsync(null));
        RefreshChannelsCommand = new AsyncRelayCommand(RefreshChannelsAsync);
    }

    public async Task RefreshChannelsAsync()
    {
        await Task.Yield();
        var all = _main.ChannelManager.GetAll();
        Channels.Clear();
        foreach (var ch in all)
        {
            Channels.Add(new ChannelCardViewModel(_main, ch));
        }
        TotalChannels = all.Count;
    }

    public void UpdateTelemetry(SystemMetrics sys, IReadOnlyList<ChannelConfig> all)
    {
        CpuPercent = sys.CpuLoadPercent;
        CpuDetails = $"{sys.CpuModel} ({sys.CpuCores} Cores)";
        RamPercent = sys.RamUsagePercent;
        RamDetails = $"{sys.RamUsedGiB} / {sys.RamTotalGiB} GiB";

        if (sys.GpuAvailable)
        {
            GpuStatus = sys.GpuModel;
            GpuDetails = $"VRAM: {sys.VramUsedGiB} / {sys.VramTotalGiB} GiB · {sys.GpuTemperatureC ?? 0}°C";
        }
        else
        {
            GpuStatus = "CPU Software Encoding";
            GpuDetails = "Fallback to libx264/libx265";
        }

        ActiveEncoders = all.Count(c => c.Direction == ChannelDirection.Encoder && _main.ChannelManager.IsRunning(c.Id));
        ActiveDecoders = all.Count(c => c.Direction == ChannelDirection.Decoder && _main.ChannelManager.IsRunning(c.Id));

        int totalKbps = 0;
        foreach (var card in Channels)
        {
            var isLive = _main.ChannelManager.IsRunning(card.Id);
            var m = _main.ChannelManager.GetMetrics(card.Id);
            card.IsStreaming = isLive;
            card.StateBadge = m.State;
            card.LastError = m.LastError;
            card.BitrateKbps = isLive && m.CurrentBitrateKbps > 0 ? (int)m.CurrentBitrateKbps : card.Config.VideoBitrateKbps;
            card.CurrentFps = isLive ? m.CurrentFps : 0;
            card.DroppedFrames = m.DroppedFrames;

            var db = Math.Max(m.AudioLevelLeftDb, m.AudioLevelRightDb);
            card.VuLevel = isLive ? Math.Clamp((db + 60.0) / 60.0 * 100.0, 0, 100) : 0;
            card.RefreshPreview();
            if (isLive) totalKbps += card.BitrateKbps;
        }
        TotalBitrateText = $"{totalKbps:N0} kbps";
    }
}

public sealed class ChannelCardViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    public ChannelConfig Config { get; }

    public Guid Id => Config.Id;
    public string Name => Config.Name;
    public ChannelDirection Direction => Config.Direction;
    public string DirectionText => Direction == ChannelDirection.Encoder ? "ENCODER" : "DECODER";
    public string InputDevice => Config.VideoDevice;
    public string SrtTarget => $"{Config.Host}:{Config.Port} ({Config.Mode})";
    public string Format => (Config.Width > 0 && Config.Height > 0)
        ? $"{Config.Width}x{Config.Height} @ {(Config.Fps > 0 ? Config.Fps.ToString() + "p" : "Auto")}"
        : (Config.Fps > 0 ? $"Auto @ {Config.Fps}p" : "Auto (Match Input)");
    public string CodecEncoder => $"{Config.Encoder.ToUpperInvariant()} / {Config.VideoCodec.ToUpperInvariant()}";
    public string OutputRoutingText
    {
        get
        {
            if (Direction == ChannelDirection.Encoder)
            {
                return !string.IsNullOrWhiteSpace(Config.VideoDevice) ? Config.VideoDevice : "Virtual / NDI";
            }

            if (Config.OutputTarget?.Contains("decklink", StringComparison.OrdinalIgnoreCase) == true ||
                Config.VideoDevice?.Contains("decklink", StringComparison.OrdinalIgnoreCase) == true)
            {
                var port = "3";
                var m = System.Text.RegularExpressions.Regex.Match($"{Config.OutputDeviceId} {Config.VideoDevice}", @"(\d+)");
                if (m.Success) port = m.Groups[1].Value;
                return $"DeckLink SDI Port {port}";
            }

            return "Desktop Preview Only";
        }
    }

    private int _currentBitrate;
    public int BitrateKbps
    {
        get => _currentBitrate > 0 ? _currentBitrate : Config.VideoBitrateKbps;
        set => SetProperty(ref _currentBitrate, value);
    }

    private double _currentFps;
    public double CurrentFps
    {
        get => _currentFps;
        set => SetProperty(ref _currentFps, value);
    }

    private long _droppedFrames;
    public long DroppedFrames
    {
        get => _droppedFrames;
        set => SetProperty(ref _droppedFrames, value);
    }

    private string _lastError = "";
    public string LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value);
    }

    private bool _isStreaming;
    public bool IsStreaming { get => _isStreaming; set => SetProperty(ref _isStreaming, value); }

    private string _stateBadge = "STOPPED";
    public string StateBadge { get => _stateBadge; set => SetProperty(ref _stateBadge, value); }

    private double _vuLevel;
    public double VuLevel { get => _vuLevel; set => SetProperty(ref _vuLevel, value); }

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

    public void RefreshPreview()
    {
        if (IsStreaming)
        {
            try
            {
                var previewPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "previews", $"{Id:N}.jpg");
                if (File.Exists(previewPath))
                {
                    using var fs = new FileStream(previewPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
            }
            catch { }
        }

        if (!IsStreaming && HasFrame)
        {
            FrameSource = null;
            HasFrame = false;
        }
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand ConfigCommand { get; }

    public ChannelCardViewModel(MainViewModel main, ChannelConfig config)
    {
        _main = main;
        Config = config;

        StartCommand = new AsyncRelayCommand(async () =>
        {
            var res = await _main.ChannelManager.StartChannelAsync(Id);
            _main.LogsVM.Log($"[CHANNEL] {Name}: {res.Message}", "Channel");
            await _main.DashboardVM.RefreshChannelsAsync();
            await _main.EncoderVM.RefreshChannelsAsync();
            await _main.DecoderVM.RefreshChannelsAsync();
        });

        StopCommand = new AsyncRelayCommand(async () =>
        {
            await _main.ChannelManager.StopChannelAsync(Id);
            _main.LogsVM.Log($"[CHANNEL] {Name}: Stopped.", "Channel");
            await _main.DashboardVM.RefreshChannelsAsync();
            await _main.EncoderVM.RefreshChannelsAsync();
            await _main.DecoderVM.RefreshChannelsAsync();
        });

        RestartCommand = new AsyncRelayCommand(async () =>
        {
            var res = await _main.ChannelManager.RestartChannelAsync(Id);
            _main.LogsVM.Log($"[CHANNEL] {Name}: Restarted ({res.Message}).", "Channel");
            await _main.DashboardVM.RefreshChannelsAsync();
            await _main.EncoderVM.RefreshChannelsAsync();
            await _main.DecoderVM.RefreshChannelsAsync();
        });

        ConfigCommand = new AsyncRelayCommand(async () => await _main.OpenChannelModalAsync(Config));
    }
}
