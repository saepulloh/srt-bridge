namespace GDI.Core.Models;

public sealed class NetworkAdapterInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string InterfaceType { get; set; } = "";
    public string Status { get; set; } = "";
    public long SpeedMbps { get; set; }
    public string MacAddress { get; set; } = "";
    public List<string> IpAddresses { get; set; } = [];
    public List<string> SubnetMasks { get; set; } = [];
    public List<string> Gateways { get; set; } = [];
    public List<string> DnsServers { get; set; } = [];
    public double RxThroughputMbps { get; set; }
    public double TxThroughputMbps { get; set; }
}
