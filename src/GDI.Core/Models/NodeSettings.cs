namespace GDI.Core.Models;

public sealed class NodeSettings
{
    public int ChannelLimit { get; set; } = 4;
    public string PreferredNetworkInterfaceId { get; set; } = "";
    public int DefaultLatencyMs { get; set; } = 250;
    public bool AutoStartEnabled { get; set; } = true;
    public string MultiviewLayout { get; set; } = "Quad"; // Single, Dual, Quad, Hexa, Octo, Nine, Sixteen
    public string SelectedFont { get; set; } = "Segoe UI";
    public string FfmpegPath { get; set; } = "tools/ffmpeg/bin/ffmpeg.exe";
    public string FfplayPath { get; set; } = "tools/ffmpeg/bin/ffplay.exe";
    public string FfprobePath { get; set; } = "tools/ffmpeg/bin/ffprobe.exe";
    public int WebRemotePort { get; set; } = 8787;
    public string WebRemoteBindAddress { get; set; } = "0.0.0.0";
    public string ApiTokenHash { get; set; } = "";
    public string StorageDirectory { get; set; } = "data";
    public string LogDirectory { get; set; } = "logs";
}
