namespace GDI.Core.Models;

public sealed class ExternalOutputConfig
{
    public bool Enabled { get; set; } = false;
    public string DeviceId { get; set; } = "decklink_3";
    public string DeviceName { get; set; } = "Blackmagic DeckLink (Port 3)";
    public int PortIndex { get; set; } = 3;
    public string VideoMode { get; set; } = "1080p50"; // 1080p50, 1080p59.94, 1080p60, 720p50, 720p60, 2160p50, 2160p60
    public string PixelFormat { get; set; } = "UYVY"; // UYVY (8-bit 4:2:2), v210 (10-bit 4:2:2), BGRA
    public string SourceType { get; set; } = "Channel"; // Channel, Program, Multiview, TestPatternA, TestPatternB
    public Guid? SourceChannelId { get; set; }
    public bool AudioEnabled { get; set; } = true;
    public int AudioSampleRate { get; set; } = 48000;
    public int AudioChannels { get; set; } = 2;
    public int AudioDelayMs { get; set; } = 0;
    public int PrerollFrames { get; set; } = 4;
    public string Status { get; set; } = "Stopped";
    public long FramesPlayed { get; set; }
    public long DroppedFrames { get; set; }
    public long LateFrames { get; set; }
    public double OutputFps { get; set; }
}
