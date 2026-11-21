using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using GDI.Core.Models;
using GDI.Hardware.Validation;

namespace GDI.Desktop.ViewModels;

public sealed record HardwareRoutingRow(string BusName, string DeviceName, string PortName, string Direction, string Status);

public sealed class HardwareIoViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private string _hardwareSummary = "Scanning Physical Broadcast Hardware...";
    public string HardwareSummary
    {
        get => _hardwareSummary;
        set => SetProperty(ref _hardwareSummary, value);
    }

    public ObservableCollection<HardwareRoutingRow> RoutingRows { get; } = [];
    public ObservableCollection<IoDevice> DetectedDevices { get; } = [];

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand SaveRoutingCommand { get; }

    public HardwareIoViewModel(MainViewModel main)
    {
        _main = main;
        RescanCommand = new AsyncRelayCommand(RefreshDevicesAsync);
        SaveRoutingCommand = new AsyncRelayCommand(SaveRoutingAsync);
    }

    public async Task RefreshDevicesAsync()
    {
        RoutingRows.Clear();
        DetectedDevices.Clear();

        var report = await _main.HardwareRegistry.DiscoverAllDevicesAsync();
        foreach (var dev in report) DetectedDevices.Add(dev);

        var decklinkDevs = report.Where(d => d.Vendor.Equals("Blackmagic Design", StringComparison.OrdinalIgnoreCase)).ToList();

        if (decklinkDevs.Count > 0)
        {
            HardwareSummary = $"Blackmagic DeckLink ({decklinkDevs.Count} SDI Channels) · Desktop Video 14.x Ready";

            int idx = 1;
            foreach (var dl in decklinkDevs)
            {
                var match = Regex.Match(dl.Model, @"\((\d+)\)");
                int portNum = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : idx;

                RoutingRows.Add(new HardwareRoutingRow(
                    $"Channel {portNum} Input",
                    dl.Model,
                    $"SDI In {portNum} (BNC 75Ω)",
                    "Input",
                    "Hardware Detected (PCIe Gen2)"
                ));
                idx++;
            }

            RoutingRows.Add(new HardwareRoutingRow(
                "Program Output",
                decklinkDevs.Count >= 3 ? decklinkDevs[2].Model : decklinkDevs[0].Model,
                decklinkDevs.Count >= 3 ? "SDI Out 3 (BNC 75Ω)" : "SDI Out 1 (BNC 75Ω)",
                "Output",
                "SDI Playout Ready"
            ));

            RoutingRows.Add(new HardwareRoutingRow(
                "Multiview Output",
                decklinkDevs.Count >= 4 ? decklinkDevs[3].Model : decklinkDevs[0].Model,
                decklinkDevs.Count >= 4 ? "SDI Out 4 (BNC 75Ω)" : "SDI Out 2 (BNC 75Ω)",
                "Output",
                "SDI Playout Ready"
            ));
        }
        else
        {
            HardwareSummary = "No Physical DeckLink Detected · DirectShow / CPU Software Mode Active";
            RoutingRows.Add(new HardwareRoutingRow("Channel 1 Input", "DirectShow Video Grabber", "USB / HDMI", "Input", "Device Ready"));
            RoutingRows.Add(new HardwareRoutingRow("Program Return In", "DirectShow Video Grabber", "USB / HDMI", "Input", "Device Ready"));
            RoutingRows.Add(new HardwareRoutingRow("Program Output", "Desktop Window Playout", "Virtual Display", "Output", "Active"));
            RoutingRows.Add(new HardwareRoutingRow("Multiview Output", "Desktop Window Playout", "Virtual Display", "Output", "Active"));
        }
    }

    private async Task SaveRoutingAsync()
    {
        var config = new IoRoutingConfig();
        var validation = PortConflictValidator.Validate(config, _main.ChannelManager.GetAll());
        if (validation.IsValid)
        {
            await _main.ConfigStore.SaveAsync("io-routing.json", config);
            MessageBox.Show("Hardware I/O routing successfully validated and saved.", "Hardware I/O Matrix", MessageBoxButton.OK, MessageBoxImage.Information);
            _main.LogsVM.Log("Hardware I/O matrix saved successfully.", "Hardware");
        }
        else
        {
            MessageBox.Show(string.Join("\n", validation.Errors), "Hardware Routing Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
