using GDI.Core.Models;

namespace GDI.SRT.Engine;

public sealed class SrtParameters
{
    public SrtMode Mode { get; set; } = SrtMode.Caller;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
    public int LatencyMs { get; set; } = 250;
    public string? StreamId { get; set; }
    public string? Passphrase { get; set; }
    public int PbKeyLength { get; set; } = 16;
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ReconnectIntervalMs { get; set; } = 2000;
    public int BufferSizeBytes { get; set; } = 8192000;
}

public static class SrtUriBuilder
{
    public static string BuildUri(SrtParameters p, bool redactPassphrase = false)
    {
        var host = p.Mode == SrtMode.Listener ? "0.0.0.0" : p.Host;
        var modeStr = p.Mode == SrtMode.Listener ? "listener" : "caller";

        var queryParams = new List<string>
        {
            $"mode={modeStr}",
            "transtype=live",
            $"latency={p.LatencyMs}",
            $"connect_timeout={p.ConnectTimeoutMs}",
            $"rcvbuf={p.BufferSizeBytes}",
            $"sndbuf={p.BufferSizeBytes}"
        };

        if (!string.IsNullOrWhiteSpace(p.Passphrase))
        {
            var pass = redactPassphrase ? "[REDACTED]" : Uri.EscapeDataString(p.Passphrase);
            queryParams.Add($"passphrase={pass}");
            queryParams.Add($"pbkeylen={p.PbKeyLength}");
        }

        if (!string.IsNullOrWhiteSpace(p.StreamId))
        {
            queryParams.Add($"streamid={Uri.EscapeDataString(p.StreamId)}");
        }

        return $"srt://{host}:{p.Port}?{string.Join("&", queryParams)}";
    }

    public static string BuildFromChannel(ChannelConfig c, bool redactPassphrase = false)
    {
        return BuildUri(new SrtParameters
        {
            Mode = c.Mode,
            Host = c.Host,
            Port = c.Port,
            LatencyMs = c.LatencyMs,
            StreamId = c.StreamId,
            Passphrase = c.Passphrase,
            PbKeyLength = c.PbKeyLength,
            ConnectTimeoutMs = c.ConnectionTimeoutMs,
            ReconnectIntervalMs = c.ReconnectIntervalMs
        }, redactPassphrase);
    }
}
