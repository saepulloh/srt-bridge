using System.Text.Json.Serialization;

namespace GDI.Core.Models;

public enum ChannelDirection
{
    Encoder,
    Decoder
}

public enum SrtMode
{
    Caller,
    Listener
}

public sealed class ChannelConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "CHANNEL 01";
    public ChannelDirection Direction { get; set; } = ChannelDirection.Encoder;
    public SrtMode Mode { get; set; } = SrtMode.Caller;
    
    // SRT Networking
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
    public int LatencyMs { get; set; } = 250;
    public string? StreamId { get; set; }
    public string? Passphrase { get; set; }
    public int PbKeyLength { get; set; } = 16; // 16 (AES-128), 24 (AES-192), 32 (AES-256)
    public int ConnectionTimeoutMs { get; set; } = 3000;
    public int ReconnectIntervalMs { get; set; } = 2000;
    public string NetworkInterfaceId { get; set; } = "";

    // Video Configuration
    public string InputBackend { get; set; } = OperatingSystem.IsWindows() ? "dshow" : "v4l2"; // dshow, v4l2, decklink, aja, magewell
    public string VideoDevice { get; set; } = OperatingSystem.IsWindows() ? "USB Video" : "/dev/video0";
    public string? VideoPortId { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public string VideoCodec { get; set; } = "h264"; // h264, h265
    public string Encoder { get; set; } = "auto"; // auto, nvenc, qsv, amf, cpu
    public string QualityProfile { get; set; } = "HighQuality"; // Balanced, HighQuality, MaximumQuality, VisuallyLossless, LowBandwidth
    public string ColorSpace { get; set; } = "bt709"; // bt709, bt2020
    public string PixelFormat { get; set; } = "yuv420p"; // yuv420p, yuv422p, yuv420p10le
    public int VideoBitrateKbps { get; set; } = 8000;

    // Audio Configuration
    public bool IncludeAudio { get; set; } = true;
    public string? AudioDevice { get; set; }
    public string AudioCodec { get; set; } = "aac";
    public int AudioBitrateKbps { get; set; } = 128;
    public int AudioSampleRate { get; set; } = 48000;

    // Decoder Output Routing
    public string OutputTarget { get; set; } = "preview"; // preview, multiview, program, decklink, aja, magewell
    public string? OutputDeviceId { get; set; }
    public string? OutputPortId { get; set; }

    // Automation & Operational State
    public bool AutoStart { get; set; } = false;
    public bool AutoTransmit { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string RuntimeState { get; set; } = "STOPPED"; // STOPPED, STARTING, STREAMING, RECEIVING, ERROR

    public ChannelConfig Clone()
    {
        return (ChannelConfig)MemberwiseClone();
    }
}
