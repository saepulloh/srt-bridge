using System.IO;
using System.Windows;
using GDI.Core.Models;
using Microsoft.Win32;

namespace GDI.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private int _channelLimit = 4;
    public int ChannelLimit { get => _channelLimit; set => SetProperty(ref _channelLimit, value); }

    private int _defaultLatency = 250;
    public int DefaultLatency { get => _defaultLatency; set => SetProperty(ref _defaultLatency, value); }

    private string _nodeApiToken = "GDI-SRT-2026";
    public string NodeApiToken { get => _nodeApiToken; set => SetProperty(ref _nodeApiToken, value); }

    private string _selectedTheme = "Dark Glass";
    public string SelectedTheme { get => _selectedTheme; set => SetProperty(ref _selectedTheme, value); }

    public IReadOnlyList<string> AvailableThemes { get; } = ["Dark Glass", "Dark Minimal", "Light Glass"];

    private string _selectedFont = "Segoe UI";
    public string SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (SetProperty(ref _selectedFont, value))
            {
                ApplyFont(value);
            }
        }
    }

    public IReadOnlyList<string> AvailableFonts { get; } =
    [
        "Segoe UI",
        "Segoe UI Variable",
        "Inter",
        "Bahnschrift",
        "Roboto",
        "Consolas",
        "Arial",
        "Trebuchet MS",
        "Cascadia Code"
    ];

    public string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "config");
    public string LogsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "logs");

    public AsyncRelayCommand SaveSettingsCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        _ = LoadSettingsAsync();
    }

    public void ApplyFont(string fontName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fontName)) return;
            var ff = new System.Windows.Media.FontFamily(fontName);
            if (Application.Current != null)
            {
                Application.Current.Resources["MasterFontFamily"] = ff;
                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.FontFamily = ff;
                }
            }
        }
        catch { }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _main.ConfigStore.LoadAsync("node-settings.json", new NodeSettings());
        ChannelLimit = settings.ChannelLimit;
        DefaultLatency = settings.DefaultLatencyMs;
        if (!string.IsNullOrWhiteSpace(settings.SelectedFont))
        {
            _selectedFont = settings.SelectedFont;
            OnPropertyChanged(nameof(SelectedFont));
            ApplyFont(_selectedFont);
        }

        if (_main.Vault.HasSecret("node_api_token"))
        {
            NodeApiToken = _main.Vault.Unprotect("node_api_token") ?? "GDI-SRT-2026";
        }
    }

    private async Task SaveSettingsAsync()
    {
        var settings = new NodeSettings
        {
            ChannelLimit = ChannelLimit > 0 ? ChannelLimit : 4,
            DefaultLatencyMs = DefaultLatency > 0 ? DefaultLatency : 250,
            SelectedFont = SelectedFont
        };

        await _main.ConfigStore.SaveAsync("node-settings.json", settings);

        if (!string.IsNullOrWhiteSpace(NodeApiToken))
        {
            _main.Vault.Protect("node_api_token", NodeApiToken.Trim());
        }

        MessageBox.Show("Settings saved successfully.", "Application Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        _main.LogsVM.Log("Application settings saved.", "Settings");
    }

    private void OpenConfigFolder()
    {
        if (Directory.Exists(ConfigPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", ConfigPath));
        }
    }
}
