using System.Collections.ObjectModel;
using GDI.Core.Models;

namespace GDI.Desktop.ViewModels;

public sealed record NetworkAdapterRow(
    string Name,
    string Description,
    string Status,
    string Speed,
    string IpAddresses,
    string TxThroughput,
    string RxThroughput
);

public sealed class NetworkViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<NetworkAdapterRow> Adapters { get; } = [];

    private string _localIpSummary = "127.0.0.1";
    public string LocalIpSummary
    {
        get => _localIpSummary;
        set => SetProperty(ref _localIpSummary, value);
    }

    private string _webRemoteLanUrl = "http://localhost:8787";
    public string WebRemoteLanUrl
    {
        get => _webRemoteLanUrl;
        set => SetProperty(ref _webRemoteLanUrl, value);
    }

    public RelayCommand RefreshCommand { get; }

    public NetworkViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(RefreshAdapters);
        RefreshAdapters();
    }

    public void RefreshAdapters()
    {
        Adapters.Clear();
        var list = _main.NetworkManager.GetAdapters();
        string firstIp = "127.0.0.1";

        foreach (var a in list)
        {
            var ips = string.Join(", ", a.IpAddresses);
            if (firstIp == "127.0.0.1" && a.IpAddresses.Count > 0 && !a.IpAddresses[0].StartsWith("127."))
            {
                firstIp = a.IpAddresses[0];
            }

            Adapters.Add(new NetworkAdapterRow(
                a.Name,
                a.Description,
                a.Status,
                $"{a.SpeedMbps} Mbps",
                ips,
                $"{a.TxThroughputMbps:F1} Mbps",
                $"{a.RxThroughputMbps:F1} Mbps"
            ));
        }

        LocalIpSummary = firstIp;
        WebRemoteLanUrl = $"http://{firstIp}:8787";
    }
}
