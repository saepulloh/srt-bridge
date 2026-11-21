using System.Text.RegularExpressions;
using GDI.Core.Models;
using GDI.Hardware.Adapters.Aja;
using GDI.Hardware.Adapters.Blackmagic;
using GDI.Hardware.Adapters.Generic;
using GDI.Hardware.Adapters.Magewell;
using GDI.Hardware.Interfaces;

namespace GDI.Hardware;

public sealed record VideoInputChoice(
    string Id,
    string DisplayName,
    string DeviceName,
    string Backend,
    string Vendor,
    string Connection,
    bool IsInUse = false,
    string? UsedByChannel = null
);

public sealed class HardwareRegistry
{
    private readonly List<IHardwareIoProvider> _providers = [];

    public HardwareRegistry(string ffmpegPath)
    {
        _providers.Add(new DeckLinkAdapter());
        _providers.Add(new AjaNtv2Adapter());
        _providers.Add(new MagewellAdapter());
        _providers.Add(new DirectShowV4l2Adapter(ffmpegPath));
    }

    public IReadOnlyList<IHardwareIoProvider> Providers => _providers;

    public async Task<List<IoDevice>> DiscoverAllDevicesAsync()
    {
        var all = new List<IoDevice>();
        foreach (var p in _providers)
        {
            try
            {
                var devs = await p.DiscoverDevicesAsync();
                all.AddRange(devs);
            }
            catch { }
        }
        return all;
    }

    public IHardwareIoProvider? FindProviderForDevice(string vendorName)
    {
        return _providers.FirstOrDefault(p => p.VendorName.Equals(vendorName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<VideoInputChoice>> GetVideoInputChoicesAsync(IReadOnlyList<ChannelConfig>? existingChannels = null, Guid? editingChannelId = null)
    {
        var choices = new List<VideoInputChoice>();
        var devices = await DiscoverAllDevicesAsync();

        // 1. Blackmagic DeckLink SDI Channels
        var decklinkDevs = devices.Where(d => d.Vendor.Equals("Blackmagic Design", StringComparison.OrdinalIgnoreCase)).ToList();
        var seenDevNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int dlIndex = 1;
        foreach (var dl in decklinkDevs)
        {
            var match = Regex.Match(dl.Model, @"\((\d+)\)");
            int portNum = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : dlIndex;
            var filterName = portNum == 1 ? "Decklink Video Capture" : $"Decklink Video Capture ({portNum})";

            if (seenDevNames.Add(filterName))
            {
                var usedBy = existingChannels?.FirstOrDefault(c =>
                    (editingChannelId == null || c.Id != editingChannelId.Value) &&
                    !string.IsNullOrWhiteSpace(c.VideoDevice) &&
                    c.VideoDevice.Equals(filterName, StringComparison.OrdinalIgnoreCase));

                bool isInUse = usedBy != null;
                string displayName = isInUse
                    ? $"{dl.Model} (SDI Port {portNum}) [IN USE: {usedBy!.Name}]"
                    : $"{dl.Model} (SDI Port {portNum})";

                choices.Add(new VideoInputChoice(
                    $"decklink_{portNum}",
                    displayName,
                    filterName,
                    "dshow",
                    "Blackmagic Design",
                    dl.Connection,
                    isInUse,
                    usedBy?.Name
                ));
            }
            dlIndex++;
        }

        // 2. Generic DirectShow / V4L2 Video Inputs
        var genericDevs = devices.Where(d => !d.Vendor.Equals("Blackmagic Design", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var gen in genericDevs)
        {
            if (gen.Ports.Any(p => p.CanInput) && seenDevNames.Add(gen.Model))
            {
                var backend = OperatingSystem.IsWindows() ? "dshow" : "v4l2";
                choices.Add(new VideoInputChoice(
                    gen.Id,
                    $"{gen.Model} ({gen.Vendor})",
                    gen.Model,
                    backend,
                    gen.Vendor,
                    gen.Connection,
                    false,
                    null
                ));
            }
        }

        // 3. Built-in Broadcast Test Source (Qualification & Offline Testing)
        choices.Add(new VideoInputChoice(
            "testsrc2",
            "Broadcast Test Pattern (1080p60 SMPTE Bars & 1kHz Tone)",
            "testsrc2",
            "lavfi",
            "Internal Generator",
            "Virtual",
            false,
            null
        ));

        return choices;
    }
}
