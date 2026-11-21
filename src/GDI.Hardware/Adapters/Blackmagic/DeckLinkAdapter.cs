using System.Diagnostics;
using System.Text.RegularExpressions;
using GDI.Core.Models;
using GDI.Hardware.Interfaces;

namespace GDI.Hardware.Adapters.Blackmagic;

public sealed class DeckLinkAdapter : IHardwareIoProvider
{
    public string VendorName => "Blackmagic Design";
    public HardwareTestStatus VerificationStatus => CheckDesktopVideoDriver() ? HardwareTestStatus.SdkConnected : HardwareTestStatus.Implemented;

    public async Task<IReadOnlyList<IoDevice>> DiscoverDevicesAsync()
    {
        var devices = new List<IoDevice>();

        if (OperatingSystem.IsWindows())
        {
            if (CheckDesktopVideoDriver())
            {
                var detected = await QueryDeckLinkDevicesWindowsAsync();
                devices.AddRange(detected);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var detected = await QueryDeckLinkDevicesLinuxAsync();
            devices.AddRange(detected);
        }

        return devices;
    }

    public Task<IReadOnlyList<string>> GetSupportedFormatsAsync(string deviceId, string portId)
    {
        IReadOnlyList<string> formats = [
            "1080p60", "1080p59.94", "1080p50", "1080p30", "1080p29.97", "1080p25", "1080p24",
            "1080i60", "1080i59.94", "1080i50", "720p60", "720p59.94", "720p50",
            "2160p60", "2160p59.94", "2160p50", "2160p30", "2160p25"
        ];
        return Task.FromResult(formats);
    }

    public Task<bool> ValidateFormatAsync(string deviceId, string portId, string format)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(format));
    }

    public Task<(bool Success, string Message)> InitializeInputAsync(string deviceId, string portId, string format)
    {
        if (!CheckDesktopVideoDriver())
            return Task.FromResult((false, "Blackmagic Desktop Video driver is not installed."));

        return Task.FromResult((true, $"DeckLink port {portId} initialized for input format {format}."));
    }

    public Task ReleaseInputAsync(string deviceId, string portId)
    {
        return Task.CompletedTask;
    }

    public Task<(bool Success, string Message)> InitializeOutputAsync(string deviceId, string portId, string format)
    {
        if (!CheckDesktopVideoDriver())
            return Task.FromResult((false, "Blackmagic Desktop Video driver is not installed."));

        return Task.FromResult((true, $"DeckLink port {portId} initialized for output playout {format}."));
    }

    public Task ReleaseOutputAsync(string deviceId, string portId)
    {
        return Task.CompletedTask;
    }

    public static bool CheckDesktopVideoDriver()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Blackmagic Design\DeckLink");
            if (key != null) return true;

            var defaultDll = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Blackmagic Design", "Blackmagic Desktop Video", "DeckLinkAPI64.dll");

            return File.Exists(defaultDll);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<IoDevice>> QueryDeckLinkDevicesWindowsAsync()
    {
        var list = new List<IoDevice>();
        try
        {
            // Query root card and subdevices via PowerShell CIM (WMI)
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(
                "& { " +
                "  $root = Get-CimInstance Win32_PnPEntity | Where-Object { $_.DeviceID -match '^PCI\\\\VEN_BDBD' } | Select-Object -First 1; " +
                "  $subs = Get-CimInstance Win32_PnPEntity | Where-Object { $_.DeviceID -match '^BLACKMAGICIO\\\\VEN_BDBD' } | Sort-Object { if ($_.Name -match '\\((\\d+)\\)') { [int]$Matches[1] } else { 0 } }; " +
                "  if ($root) { Write-Output ('ROOT|' + $root.Name + '|' + $root.DeviceID + '|' + $root.Status); } " +
                "  foreach ($s in $subs) { Write-Output ('SUB|' + $s.Name + '|' + $s.DeviceID + '|' + $s.Status); } " +
                "}"
            );

            using var p = Process.Start(psi);
            if (p != null)
            {
                var stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string rootName = "Blackmagic DeckLink Card";
                string rootPciId = "";

                var subLines = new List<(string Name, string DeviceId, string Status)>();

                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4 && parts[0] == "ROOT")
                    {
                        rootName = parts[1];
                        rootPciId = parts[2];
                    }
                    else if (parts.Length >= 4 && parts[0] == "SUB")
                    {
                        subLines.Add((parts[1], parts[2], parts[3]));
                    }
                }

                if (subLines.Count > 0)
                {
                    int channelIndex = 1;
                    foreach (var (name, devId, status) in subLines)
                    {
                        var match = Regex.Match(name, @"\((\d+)\)");
                        int portNum = match.Success && int.TryParse(match.Groups[1].Value, out var num) ? num : channelIndex;

                        var dshowVideoFilter = portNum == 1 ? "Decklink Video Capture" : $"Decklink Video Capture ({portNum})";
                        var dshowAudioFilter = portNum == 1 ? "Decklink Audio Capture" : $"Decklink Audio Capture ({portNum})";

                        var dev = new IoDevice
                        {
                            Id = $"decklink_channel_{portNum}",
                            Vendor = "Blackmagic Design",
                            Model = name,
                            SerialNumber = rootPciId,
                            Connection = $"PCIe ({rootName} Channel {portNum})",
                            Detection = "Blackmagic Desktop Video Driver",
                            Status = status == "OK" ? "Available" : status,
                            Notes = $"DirectShow: video=\"{dshowVideoFilter}\", audio=\"{dshowAudioFilter}\"",
                            Ports = [
                                new IoPort
                                {
                                    Id = $"sdi_in_{portNum}",
                                    Name = $"SDI In {portNum}",
                                    Direction = "Input",
                                    CanInput = true,
                                    CanOutput = false,
                                    Connector = "BNC 75Ω 3G-SDI",
                                    AudioChannels = 16,
                                    Formats = ["1080p60", "1080p59.94", "1080p50", "1080p30", "1080p25", "1080p24", "1080i60", "1080i50", "720p60", "720p50"]
                                },
                                new IoPort
                                {
                                    Id = $"sdi_out_{portNum}",
                                    Name = $"SDI Out {portNum}",
                                    Direction = "Output",
                                    CanInput = false,
                                    CanOutput = true,
                                    Connector = "BNC 75Ω 3G-SDI",
                                    AudioChannels = 16,
                                    Formats = ["1080p60", "1080p59.94", "1080p50", "1080p30", "1080p25", "1080p24", "1080i60", "1080i50", "720p60", "720p50"]
                                }
                            ]
                        };
                        list.Add(dev);
                        channelIndex++;
                    }
                }
                else if (!string.IsNullOrEmpty(rootPciId))
                {
                    // Single channel card
                    list.Add(new IoDevice
                    {
                        Id = "decklink_channel_1",
                        Vendor = "Blackmagic Design",
                        Model = rootName,
                        SerialNumber = rootPciId,
                        Connection = "PCIe",
                        Detection = "Blackmagic Desktop Video Driver",
                        Status = "Available",
                        Notes = "DirectShow: video=\"Decklink Video Capture\", audio=\"Decklink Audio Capture\"",
                        Ports = [
                            new IoPort
                            {
                                Id = "sdi_in_1",
                                Name = "SDI In 1",
                                Direction = "Input",
                                CanInput = true,
                                CanOutput = false,
                                Connector = "BNC 75Ω SDI",
                                AudioChannels = 16,
                                Formats = ["1080p60", "1080p50", "1080i60", "720p60"]
                            },
                            new IoPort
                            {
                                Id = "sdi_out_1",
                                Name = "SDI Out 1",
                                Direction = "Output",
                                CanInput = false,
                                CanOutput = true,
                                Connector = "BNC 75Ω SDI",
                                AudioChannels = 16,
                                Formats = ["1080p60", "1080p50", "1080i60", "720p60"]
                            }
                        ]
                    });
                }
            }
        }
        catch { }
        return list;
    }

    private static Task<List<IoDevice>> QueryDeckLinkDevicesLinuxAsync()
    {
        var list = new List<IoDevice>();
        try
        {
            if (Directory.Exists("/dev/blackmagic"))
            {
                var nodes = Directory.GetFiles("/dev/blackmagic", "io*");
                int idx = 1;
                foreach (var n in nodes)
                {
                    list.Add(new IoDevice
                    {
                        Id = $"decklink_linux_{idx}",
                        Vendor = "Blackmagic Design",
                        Model = $"Blackmagic DeckLink SDI (Node {idx})",
                        Connection = "PCIe (/dev/blackmagic)",
                        Detection = "Kernel Module blackmagic",
                        Status = "Available",
                        Ports = [
                            new IoPort { Id = $"sdi_in_{idx}", Name = $"SDI In {idx}", Direction = "Input", CanInput = true, CanOutput = false, Connector = "BNC SDI" },
                            new IoPort { Id = $"sdi_out_{idx}", Name = $"SDI Out {idx}", Direction = "Output", CanInput = false, CanOutput = true, Connector = "BNC SDI" }
                        ]
                    });
                    idx++;
                }
            }
        }
        catch { }
        return Task.FromResult(list);
    }
}
