using System.Diagnostics;
using GDI.Core.Models;
using GDI.Hardware.Interfaces;

namespace GDI.Hardware.Adapters.Generic;

public sealed class DirectShowV4l2Adapter : IHardwareIoProvider
{
    private readonly string _ffmpegPath;

    public DirectShowV4l2Adapter(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public string VendorName => OperatingSystem.IsWindows() ? "DirectShow" : "V4L2";
    public HardwareTestStatus VerificationStatus => HardwareTestStatus.DeviceDetected;

    public async Task<IReadOnlyList<IoDevice>> DiscoverDevicesAsync()
    {
        var devices = new List<IoDevice>();

        if (OperatingSystem.IsWindows())
        {
            var detected = await DetectDirectShowAsync();
            devices.AddRange(detected);
        }
        else if (OperatingSystem.IsLinux())
        {
            var detected = await DetectV4l2Async();
            devices.AddRange(detected);
        }

        return devices;
    }

    public Task<IReadOnlyList<string>> GetSupportedFormatsAsync(string deviceId, string portId)
    {
        IReadOnlyList<string> formats = ["1080p60", "1080p50", "1080p30", "720p60", "720p30"];
        return Task.FromResult(formats);
    }

    public Task<bool> ValidateFormatAsync(string deviceId, string portId, string format) => Task.FromResult(true);

    public Task<(bool Success, string Message)> InitializeInputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((true, $"DirectShow/V4L2 device {deviceId} ready."));

    public Task ReleaseInputAsync(string deviceId, string portId) => Task.CompletedTask;

    public Task<(bool Success, string Message)> InitializeOutputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((false, "DirectShow/V4L2 generic adapter does not support physical hardware output playout."));

    public Task ReleaseOutputAsync(string deviceId, string portId) => Task.CompletedTask;

    private async Task<List<IoDevice>> DetectDirectShowAsync()
    {
        var list = new List<IoDevice>();
        try
        {
            var psi = new ProcessStartInfo(_ffmpegPath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-list_devices"); psi.ArgumentList.Add("true");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("dshow");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("dummy");

            using var p = Process.Start(psi);
            if (p != null)
            {
                var stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                var lines = stderr.Split('\n');
                int idx = 1;
                foreach (var line in lines)
                {
                    if (line.Contains("(video)", StringComparison.OrdinalIgnoreCase))
                    {
                        var start = line.IndexOf('"');
                        var end = line.LastIndexOf('"');
                        var name = start >= 0 && end > start ? line.Substring(start + 1, end - start - 1) : line.Trim();

                        // DeckLinkAdapter handles all Blackmagic Design devices natively via Desktop Video driver and WMI
                        if (name.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Decklink", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string vendor = "Generic USB/Camera";
                        string connection = "USB";
                        string connector = "USB/HDMI";

                        if (name.Contains("vMix", StringComparison.OrdinalIgnoreCase))
                        {
                            vendor = "vMix Virtual Video";
                            connection = "Virtual Bus";
                            connector = "DirectShow Virtual Pin";
                        }

                        list.Add(new IoDevice
                        {
                            Id = $"dshow_video_{idx}",
                            Vendor = vendor,
                            Model = name,
                            Connection = connection,
                            Detection = "DirectShow Driver",
                            Status = "Available",
                            Ports = [
                                new IoPort
                                {
                                    Id = $"port_in_{idx}",
                                    Name = $"{name} In",
                                    Direction = "Input",
                                    CanInput = true,
                                    CanOutput = false,
                                    Connector = connector,
                                    Formats = ["1080p60", "1080p50", "1080p30", "720p60"]
                                }
                            ]
                        });
                        idx++;
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static Task<List<IoDevice>> DetectV4l2Async()
    {
        var list = new List<IoDevice>();
        try
        {
            var entries = Directory.GetFiles("/dev", "video*");
            int idx = 1;
            foreach (var dev in entries)
            {
                list.Add(new IoDevice
                {
                    Id = $"v4l2_{idx}",
                    Vendor = "V4L2 Generic",
                    Model = dev,
                    Connection = "Linux V4L2",
                    Detection = "V4L2 Kernel Driver",
                    Status = "Available",
                    Ports = [
                        new IoPort { Id = $"v4l2_port_{idx}", Name = dev, Direction = "Input", CanInput = true, CanOutput = false, Connector = "Generic" }
                    ]
                });
                idx++;
            }
        }
        catch { }
        return Task.FromResult(list);
    }
}
