using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using GDI.Core.Models;

namespace GDI.Network;

public interface INetworkManager
{
    IReadOnlyList<NetworkAdapterInfo> GetAdapters();
    NetworkAdapterInfo? GetPreferredAdapter(string preferredId);
}

public sealed class NetworkManager : INetworkManager
{
    private readonly ConcurrentDictionary<string, (long BytesSent, long BytesReceived, DateTimeOffset Time)> _prevStats = new();

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        var result = new List<NetworkAdapterInfo>();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var now = DateTimeOffset.UtcNow;

        foreach (var ni in interfaces)
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties? ipProps = null;
            try { ipProps = ni.GetIPProperties(); } catch { }

            IPv4InterfaceStatistics? ipv4Stats = null;
            try { ipv4Stats = ni.GetIPv4Statistics(); } catch { }

            var unicast = ipProps?.UnicastAddresses;
            var info = new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = ni.Name,
                Description = ni.Description,
                InterfaceType = ni.NetworkInterfaceType.ToString(),
                Status = ni.OperationalStatus.ToString(),
                SpeedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0,
                MacAddress = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                IpAddresses = unicast != null ? unicast.Select(a => a.Address.ToString()).ToList() : [],
                Gateways = ipProps?.GatewayAddresses.Select(g => g.Address.ToString()).ToList() ?? [],
                DnsServers = ipProps?.DnsAddresses.Select(d => d.ToString()).ToList() ?? [],
                SubnetMasks = unicast != null ? unicast.Where(a => a.IPv4Mask != null).Select(a => a.IPv4Mask!.ToString()).ToList() : []
            };

            // Calculate RX/TX throughput delta
            var curSent = ipv4Stats?.BytesSent ?? 0;
            var curRecv = ipv4Stats?.BytesReceived ?? 0;

            if (curSent == 0 && curRecv == 0 && OperatingSystem.IsLinux())
            {
                var (tx, rx) = ReadLinuxInterfaceBytes(ni.Name);
                if (tx > 0 || rx > 0)
                {
                    curSent = tx;
                    curRecv = rx;
                }
            }

            if (_prevStats.TryGetValue(ni.Id, out var prev))
            {
                var dt = (now - prev.Time).TotalSeconds;
                if (dt > 0.1)
                {
                    var deltaTx = Math.Max(0, curSent - prev.BytesSent);
                    var deltaRx = Math.Max(0, curRecv - prev.BytesReceived);
                    info.TxThroughputMbps = Math.Round((deltaTx * 8.0) / (dt * 1_000_000.0), 2);
                    info.RxThroughputMbps = Math.Round((deltaRx * 8.0) / (dt * 1_000_000.0), 2);
                }
            }

            _prevStats[ni.Id] = (curSent, curRecv, now);
            result.Add(info);
        }

        return result;
    }

    public NetworkAdapterInfo? GetPreferredAdapter(string preferredId)
    {
        var adapters = GetAdapters();
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var match = adapters.FirstOrDefault(a => a.Id == preferredId || a.Name.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return adapters.FirstOrDefault(a => a.Status == "Up" && a.IpAddresses.Count > 0);
    }

    private static (long Tx, long Rx) ReadLinuxInterfaceBytes(string iface)
    {
        try
        {
            if (File.Exists("/proc/net/dev"))
            {
                var lines = File.ReadAllLines("/proc/net/dev");
                foreach (var line in lines)
                {
                    if (line.Contains(iface + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            var nums = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (nums.Length >= 9 && long.TryParse(nums[0], out var rx) && long.TryParse(nums[8], out var tx))
                            {
                                return (tx, rx);
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return (0, 0);
    }
}
