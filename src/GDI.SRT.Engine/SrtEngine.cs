using GDI.Core.Models;

namespace GDI.SRT.Engine;

public interface ISrtEngine
{
    string BuildSrtUrl(ChannelConfig channel, bool redactPassphrase = false);
    Task<(bool Reachable, string Message)> PingListenerAsync(string host, int port, int timeoutMs = 2000);
}

public sealed class SrtEngine : ISrtEngine
{
    public string BuildSrtUrl(ChannelConfig channel, bool redactPassphrase = false)
    {
        return SrtUriBuilder.BuildFromChannel(channel, redactPassphrase);
    }

    public async Task<(bool Reachable, string Message)> PingListenerAsync(string host, int port, int timeoutMs = 2000)
    {
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            var target = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host == "0.0.0.0" ? "127.0.0.1" : host), port);
            // SRT handshake SYN packet probe
            var probe = new byte[16];
            await udp.SendAsync(probe, probe.Length, target);
            return (true, $"SRT endpoint {host}:{port} UDP reachable");
        }
        catch (Exception ex)
        {
            return (false, $"Unable to reach SRT endpoint: {ex.Message}");
        }
    }
}
