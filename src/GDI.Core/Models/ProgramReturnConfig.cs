namespace GDI.Core.Models;

public sealed class ProgramReturnConfig
{
    public string Role { get; set; } = "studio-send"; // studio-send or venue-receive
    public string Name { get; set; } = "PROGRAM RETURN";
    public string InputBackend { get; set; } = OperatingSystem.IsWindows() ? "dshow" : "v4l2"; // dshow, v4l2, decklink, aja, magewell
    public string VideoDevice { get; set; } = OperatingSystem.IsWindows() ? "USB Video" : "/dev/video0";
    public string? AudioDevice { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 10000;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public int BitrateKbps { get; set; } = 8000;
    public int LatencyMs { get; set; } = 250;
    public string Encoder { get; set; } = "auto";
    public string? Passphrase { get; set; }
    public int PbKeyLength { get; set; } = 16;
    public bool Fullscreen { get; set; } = true;
    public int MonitorIndex { get; set; } = 0;
    public string OutputTarget { get; set; } = "display"; // display, decklink, aja, magewell
    public string? OutputDeviceId { get; set; }
    public string? OutputPortId { get; set; }

    public ProgramReturnConfig Clone()
    {
        return (ProgramReturnConfig)MemberwiseClone();
    }
}
