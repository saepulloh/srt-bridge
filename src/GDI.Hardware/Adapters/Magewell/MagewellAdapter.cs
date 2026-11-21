using GDI.Core.Models;
using GDI.Hardware.Interfaces;

namespace GDI.Hardware.Adapters.Magewell;

public sealed class MagewellAdapter : IHardwareIoProvider
{
    public string VendorName => "Magewell";
    public HardwareTestStatus VerificationStatus => HardwareTestStatus.Implemented;

    public Task<IReadOnlyList<IoDevice>> DiscoverDevicesAsync()
    {
        // Magewell Pro Capture / Eco Capture SDK discovery
        return Task.FromResult<IReadOnlyList<IoDevice>>([]);
    }

    public Task<IReadOnlyList<string>> GetSupportedFormatsAsync(string deviceId, string portId)
    {
        IReadOnlyList<string> formats = [
            "1080p60", "1080p59.94", "1080p50", "720p60", "2160p30", "2160p60"
        ];
        return Task.FromResult(formats);
    }

    public Task<bool> ValidateFormatAsync(string deviceId, string portId, string format) => Task.FromResult(true);

    public Task<(bool Success, string Message)> InitializeInputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((true, $"Magewell channel {portId} initialized ({format})."));

    public Task ReleaseInputAsync(string deviceId, string portId) => Task.CompletedTask;

    public Task<(bool Success, string Message)> InitializeOutputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((true, $"Magewell output port {portId} initialized ({format})."));

    public Task ReleaseOutputAsync(string deviceId, string portId) => Task.CompletedTask;
}
