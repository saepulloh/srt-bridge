namespace GDI.Core.Models;

public sealed class ChannelMetrics
{
    public Guid ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public string State { get; set; } = "STOPPED";
    public double CurrentBitrateKbps { get; set; }
    public double CurrentFps { get; set; }
    public long DroppedFrames { get; set; }
    public long TotalFrames { get; set; }
    public double CpuPercent { get; set; }
    
    // SRT Metrics
    public double SrtRttMs { get; set; }
    public double SrtPacketLossPercent { get; set; }
    public double SrtBandwidthMbps { get; set; }
    public double SrtJitterMs { get; set; }
    public long BytesTransferred { get; set; }

    // Audio telemetry
    public float AudioLevelLeftDb { get; set; } = -60.0f;
    public float AudioLevelRightDb { get; set; } = -60.0f;

    public string? LastError { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
