namespace GDI.Core.Models;

public sealed class IoRoutingConfig
{
    public List<IoDevice> Devices { get; set; } = [];
    public List<InputAssignment> Inputs { get; set; } = [];
    public List<OutputAssignment> Outputs { get; set; } = [];
}

public sealed class IoDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Vendor { get; set; } = "Blackmagic Design"; // Blackmagic Design, AJA, Magewell, DirectShow, Generic
    public string Model { get; set; } = "DeckLink Quad 2";
    public string SerialNumber { get; set; } = "";
    public string Connection { get; set; } = "PCIe"; // PCIe, USB, Thunderbolt, Virtual
    public List<IoPort> Ports { get; set; } = [];
    public string Detection { get; set; } = "sdk"; // sdk, manual, driver
    public string Status { get; set; } = "Available"; // Available, InUse, Offline, Unqualified
    public string Notes { get; set; } = "";
}

public sealed class IoPort
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "SDI 1";
    public string Direction { get; set; } = "Bidirectional"; // Input, Output, Bidirectional
    public string Connector { get; set; } = "BNC SDI"; // BNC SDI, HDMI, Optical, DisplayPort
    public bool CanInput { get; set; } = true;
    public bool CanOutput { get; set; } = true;
    public List<string> Formats { get; set; } = ["1080p50", "1080p59.94", "1080p60", "720p60", "2160p30"];
    public int AudioChannels { get; set; } = 16;
}

public sealed class InputAssignment
{
    public string SourceId { get; set; } = ""; // Channel GUID or "return"
    public string DeviceId { get; set; } = "";
    public string PortId { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class OutputAssignment
{
    public string Output { get; set; } = "program"; // program or multiview
    public string DeviceId { get; set; } = "";
    public string PortId { get; set; } = "";
    public string Format { get; set; } = "1080p60";
    public bool EmbeddedAudio { get; set; } = true;
}
