namespace GDI.Core.Models;

public sealed class ChannelMetrics
{
    public Guid ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public string State { get; set; } = "STOPPED";
    public string ActiveBackend { get; set; } = "Unknown";
    public string ActiveCodec { get; set; } = "H.264";
    public string PixelFormat { get; set; } = "yuv420p";
    public double CurrentBitrateKbps { get; set; }
    public double CurrentFps { get; set; }
    public double InputFps { get; set; }
    public double DecoderFps { get; set; }
    public double PreviewFps { get; set; }
    public long DroppedFrames { get; set; }
    public long LateFrames { get; set; }
    public long TotalFrames { get; set; }
    public double CpuPercent { get; set; }
    public int QueueDepthFrames { get; set; }
    
    // SRT Metrics
    public double SrtRttMs { get; set; }
    public double SrtPacketLossPercent { get; set; }
    public double SrtBandwidthMbps { get; set; }
    public double SrtJitterMs { get; set; }
    public long BytesTransferred { get; set; }

    // Audio DSP Telemetry (Decoded PCM Peak & RMS dBFS)
    public float AudioLevelLeftDb { get; set; } = -60.0f;
    public float AudioLevelRightDb { get; set; } = -60.0f;
    public float RmsLevelLeftDb { get; set; } = -60.0f;
    public float RmsLevelRightDb { get; set; } = -60.0f;
    public float PeakHoldLeftDb { get; set; } = -60.0f;
    public float PeakHoldRightDb { get; set; } = -60.0f;
    public bool IsClippingLeft { get; set; }
    public bool IsClippingRight { get; set; }
    public bool IsAudioSilent { get; set; } = true;

    // Compatibility & Alias properties
    public float PeakLeftDb { get; set; } = -60.0f;
    public float PeakRightDb { get; set; } = -60.0f;
    public float RmsLeftDb { get => RmsLevelLeftDb; set => RmsLevelLeftDb = value; }
    public float RmsRightDb { get => RmsLevelRightDb; set => RmsLevelRightDb = value; }
    public float AudioPeakHoldLeftDb { get => PeakHoldLeftDb; set => PeakHoldLeftDb = value; }
    public float AudioPeakHoldRightDb { get => PeakHoldRightDb; set => PeakHoldRightDb = value; }
    public bool IsAudioClippingLeft { get => IsClippingLeft; set => IsClippingLeft = value; }
    public bool IsAudioClippingRight { get => IsClippingRight; set => IsClippingRight = value; }
    public string ActiveHardwareBackend { get => ActiveBackend; set => ActiveBackend = value; }

    // Video Geometry & Diagnostics
    public string SourceResolution { get; set; } = "1920x1080 @ 50 FPS";
    public string DecoderResolution { get; set; } = "1920x1080 @ 50 FPS";
    public string MultiviewResolution { get; set; } = "960x540 @ 30 FPS";
    public string ScalingStatus { get; set; } = "ENABLED";
    public int FrameBufferSize { get; set; }
    public int FrameStride { get; set; }

    public string? LastError { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
