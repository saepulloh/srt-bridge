using GDI.Core.Models;
using GDI.ProgramReturn;

namespace GDI.Desktop.ViewModels;

public sealed class ProgramReturnViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private ProgramReturnConfig _config = new();
    public ProgramReturnConfig Config
    {
        get => _config;
        set => SetProperty(ref _config, value);
    }

    private string _statusText = "Status: Stopped";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public AsyncRelayCommand StartReturnCommand { get; }
    public AsyncRelayCommand StopReturnCommand { get; }

    public ProgramReturnViewModel(MainViewModel main)
    {
        _main = main;

        StartReturnCommand = new AsyncRelayCommand(async () =>
        {
            var res = await _main.ProgramReturnEngine.StartAsync(Config);
            IsRunning = res.Success;
            StatusText = res.Success ? "Status: Transmitting Active (Studio ➔ Venue)" : $"Status: Failed ({res.Message})";
            _main.LogsVM.Log($"[PROGRAM RETURN] {res.Message}", "ProgramReturn");
        });

        StopReturnCommand = new AsyncRelayCommand(async () =>
        {
            await _main.ProgramReturnEngine.StopAsync();
            IsRunning = false;
            StatusText = "Status: Stopped";
            _main.LogsVM.Log("[PROGRAM RETURN] Stopped.", "ProgramReturn");
        });

        _ = LoadInitialConfigAsync();
    }

    private async Task LoadInitialConfigAsync()
    {
        Config = await _main.ConfigStore.LoadAsync("program-return.json", new ProgramReturnConfig());
        var status = _main.ProgramReturnEngine.GetStatus();
        IsRunning = status.IsRunning;
        StatusText = IsRunning ? "Status: Transmitting Active" : "Status: Stopped";
    }
}
