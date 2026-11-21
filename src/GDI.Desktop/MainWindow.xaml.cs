using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GDI.Core.Events;
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
using GDI.Desktop.ViewModels;

namespace GDI.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _telemetryTimer;
    private Process? _nodeProcess;
    private bool _isFullscreen;
    private WindowState _prevWindowState;

    public MainWindow()
    {
        InitializeComponent();

        // Resolve FFmpeg & FFplay binary paths
        var baseDir = AppContext.BaseDirectory;
        var ffmpegPath = Path.Combine(baseDir, "tools", "ffmpeg", "bin", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg", "bin", "ffmpeg.exe");
            ffmpegPath = File.Exists(fallback) ? fallback : "ffmpeg";
        }

        var ffplayPath = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffplay.exe");
        if (!File.Exists(ffplayPath)) ffplayPath = "ffplay";

        // Initialize Core Broadcast Engine singletons
        var configStore = new JsonConfigurationStore();
        var vault = new DpapiSecretVault();
        var eventBus = new BroadcastEventBus();
        var systemMonitor = new SystemMonitor();
        var networkManager = new NetworkManager();
        var hardwareRegistry = new HardwareRegistry(ffmpegPath);
        var multiviewEngine = new MultiviewEngine();
        var encoderEngine = new EncoderEngine(ffmpegPath, eventBus);
        var decoderEngine = new DecoderEngine(ffplayPath, ffmpegPath, eventBus);
        var programReturnEngine = new ProgramReturnEngine(ffmpegPath, ffplayPath, configStore, vault);
        var channelManager = new ChannelManager(configStore, encoderEngine, decoderEngine, vault, eventBus);

        // Instantiate Master MVVM ViewModel
        _viewModel = new MainViewModel(
            configStore,
            vault,
            eventBus,
            systemMonitor,
            networkManager,
            hardwareRegistry,
            multiviewEngine,
            encoderEngine,
            decoderEngine,
            programReturnEngine,
            channelManager
        );

        DataContext = _viewModel;

        // Telemetry Update Loop (1-second tick)
        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _telemetryTimer.Tick += async (_, _) =>
        {
            try
            {
                await _viewModel.UpdateTelemetryAsync();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("desktop_error.log", $"[{DateTime.Now:HH:mm:ss}] Telemetry tick error: {ex}\r\n"); } catch { }
            }
        };
        _telemetryTimer.Start();

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.LogsVM.Log("GDI SRT Embedded Native Desktop v1.0.0 initialized.", "System");
            _viewModel.LogsVM.Log("Dark Glassmorphism UI & Broadcast Control Engine active.", "System");

            await EnsureNodeServiceRunningAsync();

            await _viewModel.DashboardVM.RefreshChannelsAsync();
            await _viewModel.EncoderVM.RefreshChannelsAsync();
            await _viewModel.DecoderVM.RefreshChannelsAsync();
            await _viewModel.HardwareIoVM.RefreshDevicesAsync();
            await _viewModel.RefreshVideoChoicesAsync();
            _viewModel.MultiviewVM.RefreshTiles();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("desktop_error.log", $"[{DateTime.Now:HH:mm:ss}] OnWindowLoaded error: {ex}\r\n"); } catch { }
            MessageBox.Show($"UI initialization error: {ex.Message}", "GDI Desktop Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task EnsureNodeServiceRunningAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var res = await client.GetAsync("http://localhost:8787/api/health").ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                Dispatcher.Invoke(() =>
                {
                    _viewModel.NodeServiceStatus = "Running (Port 8787)";
                    _viewModel.LogsVM.Log("[NODE SERVICE] Detected active GDI Node background service on port 8787.", "Node");
                });
                return;
            }
        }
        catch { }

        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "GDI.Node.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "GDI-SRT-Embedded-Windows-x64", "GDI.Node.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "GDI.Node", "bin", "Release", "net8.0", "GDI.Node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GDI", "SRT Embedded", "GDI.Node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GDI SRT Embedded", "GDI.Node.exe")
            };

            var exePath = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!
                };
                _nodeProcess = Process.Start(psi);
                Dispatcher.Invoke(() =>
                {
                    _viewModel.NodeServiceStatus = $"Running (PID {_nodeProcess?.Id})";
                    _viewModel.LogsVM.Log($"[NODE SERVICE] Auto-launched Web Remote service (PID {_nodeProcess?.Id}) on port 8787.", "Node");
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => _viewModel.LogsVM.Log($"[NODE SERVICE] Failed to start node: {ex.Message}", "Error"));
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            File.AppendAllText("desktop_error.log", $"[{DateTime.Now:HH:mm:ss}] OnWindowClosing triggered. StackTrace:\r\n{Environment.StackTrace}\r\n");
        }
        catch { }

        _telemetryTimer.Stop();
        if (_nodeProcess != null && !_nodeProcess.HasExited)
        {
            try { _nodeProcess.Kill(true); } catch { }
        }
    }

    // Window Chrome Buttons
    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        try { File.AppendAllText("desktop_error.log", $"[{DateTime.Now:HH:mm:ss}] OnCloseClicked called.\r\n"); } catch { }
        Close();
    }

    // Keyboard Shortcuts (Ctrl+1..0, F11)
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1: _viewModel.NavigateTo("Dashboard"); e.Handled = true; break;
                case Key.D2: _viewModel.NavigateTo("Encoder"); e.Handled = true; break;
                case Key.D3: _viewModel.NavigateTo("Decoder"); e.Handled = true; break;
                case Key.D4: _viewModel.NavigateTo("Multiview"); e.Handled = true; break;
                case Key.D5: _viewModel.NavigateTo("Program Return"); e.Handled = true; break;
                case Key.D6: _viewModel.NavigateTo("Hardware I/O"); e.Handled = true; break;
                case Key.D7: _viewModel.NavigateTo("Network"); e.Handled = true; break;
                case Key.D8: _viewModel.NavigateTo("System Monitor"); e.Handled = true; break;
                case Key.D9: _viewModel.NavigateTo("Logs"); e.Handled = true; break;
                case Key.D0: _viewModel.NavigateTo("Settings"); e.Handled = true; break;
            }
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    public void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _prevWindowState = WindowState;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            _viewModel.NavigateTo("Multiview");
            _viewModel.LogsVM.Log("Entered Multiview Fullscreen mode (F11).", "Multiview");
        }
        else
        {
            WindowState = _prevWindowState;
            _isFullscreen = false;
            _viewModel.LogsVM.Log("Exited Multiview Fullscreen mode.", "Multiview");
        }
    }
}
