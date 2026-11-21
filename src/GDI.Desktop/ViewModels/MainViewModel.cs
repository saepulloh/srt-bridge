using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Services;
using GDI.Core.Storage;
using GDI.Decoder;
using GDI.Encoder;
using GDI.Hardware;
using GDI.Multiview;
using GDI.Network;
using GDI.Node.Services;
using GDI.ProgramReturn;

namespace GDI.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    // Core Broadcast Engines
    public IConfigurationStore ConfigStore { get; }
    public ISecretVault Vault { get; }
    public BroadcastEventBus EventBus { get; }
    public ISystemMonitor SystemMonitor { get; }
    public INetworkManager NetworkManager { get; }
    public HardwareRegistry HardwareRegistry { get; }
    public IMultiviewEngine MultiviewEngine { get; }
    public IEncoderEngine EncoderEngine { get; }
    public IDecoderEngine DecoderEngine { get; }
    public IProgramReturnEngine ProgramReturnEngine { get; }
    public IChannelManager ChannelManager { get; }

    // Navigation Pages
    private object? _currentPage;
    public object? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    private string _activePageName = "Dashboard";
    public string ActivePageName
    {
        get => _activePageName;
        set => SetProperty(ref _activePageName, value);
    }

    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (SetProperty(ref _isSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
            }
        }
    }

    public double SidebarWidth => IsSidebarCollapsed ? 64 : 220;

    // Global Status & Telemetry
    private string _utcTime = "";
    public string UtcTime
    {
        get => _utcTime;
        set => SetProperty(ref _utcTime, value);
    }

    private string _cpuHeader = "0%";
    public string CpuHeader
    {
        get => _cpuHeader;
        set => SetProperty(ref _cpuHeader, value);
    }

    private string _ramHeader = "0 / 0 GB";
    public string RamHeader
    {
        get => _ramHeader;
        set => SetProperty(ref _ramHeader, value);
    }

    private string _gpuHeader = "GPU Ready";
    public string GpuHeader
    {
        get => _gpuHeader;
        set => SetProperty(ref _gpuHeader, value);
    }

    private int _activeEncodersCount;
    public int ActiveEncodersCount
    {
        get => _activeEncodersCount;
        set => SetProperty(ref _activeEncodersCount, value);
    }

    private int _activeDecodersCount;
    public int ActiveDecodersCount
    {
        get => _activeDecodersCount;
        set => SetProperty(ref _activeDecodersCount, value);
    }

    private string _nodeServiceStatus = "Running (Port 8787)";
    public string NodeServiceStatus
    {
        get => _nodeServiceStatus;
        set => SetProperty(ref _nodeServiceStatus, value);
    }

    private string _decklinkStatus = "Blackmagic DeckLink Duo 2 (4 SDI Ports Ready)";
    public string DecklinkStatus
    {
        get => _decklinkStatus;
        set => SetProperty(ref _decklinkStatus, value);
    }

    private string _networkSpeedText = "0 Mbps";
    public string NetworkSpeedText
    {
        get => _networkSpeedText;
        set => SetProperty(ref _networkSpeedText, value);
    }

    // Modal Channel Config Overlay
    private bool _isChannelModalOpen;
    public bool IsChannelModalOpen
    {
        get => _isChannelModalOpen;
        set => SetProperty(ref _isChannelModalOpen, value);
    }

    private string _channelModalTitle = "Configure Channel";
    public string ChannelModalTitle
    {
        get => _channelModalTitle;
        set => SetProperty(ref _channelModalTitle, value);
    }

    // Channel Form Properties
    public Guid? EditingChannelId { get; set; }
    public string FormChanName { get; set; } = "PGM 01";
    public ChannelDirection FormDirection { get; set; } = ChannelDirection.Encoder;
    public int FormDirectionIndex
    {
        get => (int)FormDirection;
        set { if (value >= 0) FormDirection = (ChannelDirection)value; }
    }

    public SrtMode FormMode { get; set; } = SrtMode.Caller;
    public int FormModeIndex
    {
        get => (int)FormMode;
        set { if (value >= 0) FormMode = (SrtMode)value; }
    }

    public string FormHost { get; set; } = "127.0.0.1";
    public int FormPort { get; set; } = 9001;
    public int FormBitrate { get; set; } = 8000;
    public string FormEncoder { get; set; } = "auto";
    public string FormVideoCodec { get; set; } = "h264";
    public string FormResolution { get; set; } = "Auto (Match Input Source)";
    private string _formFpsString = "Auto (Match Input Source)";
    public string FormFpsString
    {
        get => _formFpsString;
        set
        {
            if (SetProperty(ref _formFpsString, value))
            {
                OnPropertyChanged(nameof(FormFps));
            }
        }
    }
    public int FormFps
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_formFpsString) || _formFpsString.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
                return 0;
            var match = Regex.Match(_formFpsString, @"\d+");
            return match.Success && int.TryParse(match.Value, out var fps) ? fps : 0;
        }
        set
        {
            _formFpsString = value > 0 ? value.ToString() : "Auto (Match Input Source)";
            OnPropertyChanged(nameof(FormFpsString));
            OnPropertyChanged(nameof(FormFps));
        }
    }
    public string FormPassphrase { get; set; } = "";
    public VideoInputChoice? FormSelectedVideoInput { get; set; }
    public ObservableCollection<VideoInputChoice> VideoInputChoices { get; } = [];

    // Commands
    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand OpenWebRemoteCommand { get; }
    public AsyncRelayCommand RescanHardwareCommand { get; }
    public RelayCommand CloseChannelModalCommand { get; }
    public AsyncRelayCommand SaveChannelModalCommand { get; }

    // Sub ViewModels
    public DashboardViewModel DashboardVM { get; }
    public EncoderViewModel EncoderVM { get; }
    public DecoderViewModel DecoderVM { get; }
    public MultiviewViewModel MultiviewVM { get; }
    public ProgramReturnViewModel ProgramReturnVM { get; }
    public HardwareIoViewModel HardwareIoVM { get; }
    public NetworkViewModel NetworkVM { get; }
    public SystemMonitorViewModel SystemMonitorVM { get; }
    public LogsViewModel LogsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        IConfigurationStore configStore,
        ISecretVault vault,
        BroadcastEventBus eventBus,
        ISystemMonitor systemMonitor,
        INetworkManager networkManager,
        HardwareRegistry hardwareRegistry,
        IMultiviewEngine multiviewEngine,
        IEncoderEngine encoderEngine,
        IDecoderEngine decoderEngine,
        IProgramReturnEngine programReturnEngine,
        IChannelManager channelManager)
    {
        ConfigStore = configStore;
        Vault = vault;
        EventBus = eventBus;
        SystemMonitor = systemMonitor;
        NetworkManager = networkManager;
        HardwareRegistry = hardwareRegistry;
        MultiviewEngine = multiviewEngine;
        EncoderEngine = encoderEngine;
        DecoderEngine = decoderEngine;
        ProgramReturnEngine = programReturnEngine;
        ChannelManager = channelManager;

        // Initialize Page ViewModels
        DashboardVM = new DashboardViewModel(this);
        EncoderVM = new EncoderViewModel(this);
        DecoderVM = new DecoderViewModel(this);
        MultiviewVM = new MultiviewViewModel(this);
        ProgramReturnVM = new ProgramReturnViewModel(this);
        HardwareIoVM = new HardwareIoViewModel(this);
        NetworkVM = new NetworkViewModel(this);
        SystemMonitorVM = new SystemMonitorViewModel(this);
        LogsVM = new LogsViewModel(this);
        SettingsVM = new SettingsViewModel(this);

        // Set default page
        CurrentPage = DashboardVM;
        ActivePageName = "Dashboard";

        // Commands
        NavigateCommand = new RelayCommand(p => NavigateTo(p?.ToString() ?? "Dashboard"));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        OpenWebRemoteCommand = new RelayCommand(() =>
        {
            try
            {
                EnsureNodeServiceRunning();
                Process.Start(new ProcessStartInfo("http://localhost:8787") { UseShellExecute = true });
            }
            catch { }
        });
        Task.Run(EnsureNodeServiceRunning);
        RescanHardwareCommand = new AsyncRelayCommand(async () =>
        {
            await HardwareIoVM.RefreshDevicesAsync();
            await RefreshVideoChoicesAsync();
            LogsVM.Log("Hardware rescan completed.", "Hardware");
        });
        CloseChannelModalCommand = new RelayCommand(() => IsChannelModalOpen = false);
        SaveChannelModalCommand = new AsyncRelayCommand(SaveChannelFromModalAsync);

        // Subscribe to broadcast events
        EventBus.Subscribe<string>(msg =>
        {
            Application.Current.Dispatcher.Invoke(() => LogsVM.Log(msg, "Broadcast"));
        });
    }

    public void NavigateTo(string pageName)
    {
        ActivePageName = pageName;
        CurrentPage = pageName switch
        {
            "Dashboard" => DashboardVM,
            "Encoder" => EncoderVM,
            "Decoder" => DecoderVM,
            "Multiview" => MultiviewVM,
            "Program Return" => ProgramReturnVM,
            "Hardware I/O" => HardwareIoVM,
            "Network" => NetworkVM,
            "System Monitor" => SystemMonitorVM,
            "Logs" => LogsVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };

        // Trigger page-specific refresh when navigated
        if (CurrentPage is MultiviewViewModel mv) mv.RefreshTiles();
        else if (CurrentPage is NetworkViewModel net) net.RefreshAdapters();
        else if (CurrentPage is HardwareIoViewModel hw) _ = hw.RefreshDevicesAsync();
    }

    public async Task RefreshVideoChoicesAsync(Guid? editingChannelId = null)
    {
        var existingChannels = ChannelManager.GetAll();
        var choices = await HardwareRegistry.GetVideoInputChoicesAsync(existingChannels, editingChannelId);
        VideoInputChoices.Clear();
        foreach (var c in choices)
        {
            // Do not show DeckLink devices that are already claimed by another channel
            if (!c.IsInUse)
            {
                VideoInputChoices.Add(c);
            }
        }
    }

    public async Task OpenChannelModalAsync(ChannelConfig? channel)
    {
        await RefreshVideoChoicesAsync(channel?.Id);
        EditingChannelId = channel?.Id;

        if (channel != null)
        {
            ChannelModalTitle = $"Configure Channel — {channel.Name}";
            FormChanName = channel.Name;
            FormDirection = channel.Direction;
            FormMode = channel.Mode;
            FormHost = channel.Host;
            FormPort = channel.Port;
            FormBitrate = channel.VideoBitrateKbps;
            FormEncoder = channel.Encoder;
            FormVideoCodec = channel.VideoCodec ?? "h264";
            FormResolution = (channel.Width > 0 && channel.Height > 0)
                ? $"{channel.Width}x{channel.Height}"
                : "Auto (Match Input Source)";
            FormFps = channel.Fps;
            FormPassphrase = channel.Passphrase ?? "";
            FormSelectedVideoInput = VideoInputChoices.FirstOrDefault(c => c.DeviceName.Equals(channel.VideoDevice, StringComparison.OrdinalIgnoreCase))
                ?? VideoInputChoices.FirstOrDefault();
        }
        else
        {
            var nextPort = 9001 + ChannelManager.GetAll().Count;
            ChannelModalTitle = "Add New Channel";
            FormChanName = $"CHANNEL {ChannelManager.GetAll().Count + 1:D2}";
            FormDirection = ChannelDirection.Encoder;
            FormMode = SrtMode.Caller;
            FormHost = "127.0.0.1";
            FormPort = nextPort;
            FormBitrate = 8000;
            FormEncoder = "auto";
            FormVideoCodec = "h264";
            FormResolution = "Auto (Match Input Source)";
            FormFps = 0; // Auto
            FormPassphrase = "";
            FormSelectedVideoInput = VideoInputChoices.FirstOrDefault();
        }

        OnPropertyChanged(nameof(FormChanName));
        OnPropertyChanged(nameof(FormDirection));
        OnPropertyChanged(nameof(FormDirectionIndex));
        OnPropertyChanged(nameof(FormMode));
        OnPropertyChanged(nameof(FormModeIndex));
        OnPropertyChanged(nameof(FormHost));
        OnPropertyChanged(nameof(FormPort));
        OnPropertyChanged(nameof(FormBitrate));
        OnPropertyChanged(nameof(FormEncoder));
        OnPropertyChanged(nameof(FormVideoCodec));
        OnPropertyChanged(nameof(FormResolution));
        OnPropertyChanged(nameof(FormFpsString));
        OnPropertyChanged(nameof(FormFps));
        OnPropertyChanged(nameof(FormPassphrase));
        OnPropertyChanged(nameof(FormSelectedVideoInput));
        IsChannelModalOpen = true;
    }

    private async Task SaveChannelFromModalAsync()
    {
        var ch = EditingChannelId.HasValue
            ? ChannelManager.Get(EditingChannelId.Value) ?? new ChannelConfig()
            : new ChannelConfig();

        ch.Name = FormChanName;
        ch.Direction = FormDirection;
        ch.Mode = FormMode;
        ch.Host = FormHost;
        ch.Port = FormPort;
        ch.VideoBitrateKbps = FormBitrate;
        ch.Encoder = FormEncoder;
        ch.VideoCodec = FormVideoCodec;
        ch.Fps = FormFps;
        ch.Passphrase = FormPassphrase;

        if (string.IsNullOrWhiteSpace(FormResolution) || FormResolution.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
        {
            ch.Width = 0;
            ch.Height = 0;
        }
        else
        {
            var resParts = FormResolution.Split('x');
            if (resParts.Length == 2 && int.TryParse(resParts[0], out var w) && int.TryParse(resParts[1], out var h))
            {
                ch.Width = w;
                ch.Height = h;
            }
            else
            {
                ch.Width = 0;
                ch.Height = 0;
            }
        }

        if (FormSelectedVideoInput != null)
        {
            bool isDecklink = FormSelectedVideoInput.DeviceName.Contains("Decklink", StringComparison.OrdinalIgnoreCase) ||
                              FormSelectedVideoInput.DeviceName.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase);

            if (isDecklink)
            {
                var existingConflict = ChannelManager.GetAll().FirstOrDefault(c =>
                    c.Id != ch.Id &&
                    !string.IsNullOrWhiteSpace(c.VideoDevice) &&
                    c.VideoDevice.Equals(FormSelectedVideoInput.DeviceName, StringComparison.OrdinalIgnoreCase));

                if (existingConflict != null)
                {
                    LogsVM.Log($"Device '{FormSelectedVideoInput.DeviceName}' is already used by channel '{existingConflict.Name}'. Operation cancelled to prevent duplicate DeckLink usage.", "Hardware Conflict");
                    return;
                }
            }

            ch.VideoDevice = FormSelectedVideoInput.DeviceName;
            ch.InputBackend = FormSelectedVideoInput.Backend;
            ch.AudioDevice = isDecklink
                ? FormSelectedVideoInput.DeviceName.Replace("Video", "Audio")
                : null;
        }

        await ChannelManager.AddOrUpdateAsync(ch);
        IsChannelModalOpen = false;
        await DashboardVM.RefreshChannelsAsync();
        await EncoderVM.RefreshChannelsAsync();
        await DecoderVM.RefreshChannelsAsync();
        MultiviewVM.RefreshTiles();
        LogsVM.Log($"Saved channel configuration for {ch.Name}.", "Channel");
    }

    public async Task UpdateTelemetryAsync()
    {
        UtcTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        try
        {
            var sys = await SystemMonitor.GetMetricsAsync();
            CpuHeader = $"{Math.Round(sys.CpuLoadPercent)}%";
            RamHeader = $"{sys.RamUsedGiB} / {sys.RamTotalGiB} GB";
            GpuHeader = sys.GpuAvailable ? $"{sys.GpuModel}" : "CPU Software";

            var all = ChannelManager.GetAll();
            ActiveEncodersCount = all.Count(c => c.Direction == ChannelDirection.Encoder && ChannelManager.IsRunning(c.Id));
            ActiveDecodersCount = all.Count(c => c.Direction == ChannelDirection.Decoder && ChannelManager.IsRunning(c.Id));

            var adapters = NetworkManager.GetAdapters();
            var primary = adapters.FirstOrDefault(a => a.Status.Equals("Up", StringComparison.OrdinalIgnoreCase));
            if (primary != null)
            {
                NetworkSpeedText = $"TX: {primary.TxThroughputMbps:F1} | RX: {primary.RxThroughputMbps:F1} Mbps";
            }

            // Propagate updates to sub viewmodels
            DashboardVM.UpdateTelemetry(sys, all);
            EncoderVM.UpdateTelemetry(all);
            DecoderVM.UpdateTelemetry(all);
            SystemMonitorVM.UpdateTelemetry(sys, all);
            MultiviewVM.UpdateTelemetry(all);
        }
        catch { }
    }

    private static void EnsureNodeServiceRunning()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var ar = client.BeginConnect("127.0.0.1", 8787, null, null);
            bool success = ar.AsyncWaitHandle.WaitOne(300);
            if (success && client.Connected)
            {
                client.EndConnect(ar);
                return;
            }
        }
        catch { }

        try
        {
            var nodeExe = Path.Combine(AppContext.BaseDirectory, "GDI.Node.exe");
            if (!File.Exists(nodeExe))
            {
                nodeExe = Path.Combine(Directory.GetCurrentDirectory(), "GDI.Node.exe");
            }
            if (File.Exists(nodeExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nodeExe,
                    WorkingDirectory = Path.GetDirectoryName(nodeExe),
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Thread.Sleep(500);
            }
        }
        catch { }
    }
}
