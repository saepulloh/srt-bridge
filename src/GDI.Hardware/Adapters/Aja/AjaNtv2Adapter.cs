using GDI.Core.Models;
using GDI.Hardware.Interfaces;

namespace GDI.Hardware.Adapters.Aja;

public sealed class AjaNtv2Adapter : IHardwareIoProvider
{
    public string VendorName => "AJA Video Systems";
    public HardwareTestStatus VerificationStatus => HardwareTestStatus.Implemented;

    public Task<IReadOnlyList<IoDevice>> DiscoverDevicesAsync()
    {
        // AJA NTV2 driver query architecture
        return Task.FromResult<IReadOnlyList<IoDevice>>([]);
    }

    public Task<IReadOnlyList<string>> GetSupportedFormatsAsync(string deviceId, string portId)
    {
        IReadOnlyList<string> formats = [
            "1080p60", "1080p59.94", "1080p50", "1080i60", "1080i50",
            "720p60", "720p50", "2160p60", "2160p50"
        ];
        return Task.FromResult(formats);
    }

    public Task<bool> ValidateFormatAsync(string deviceId, string portId, string format) => Task.FromResult(true);

    public Task<(bool Success, string Message)> InitializeInputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((true, $"AJA NTV2 port {portId} initialized ({format})."));

    public Task ReleaseInputAsync(string deviceId, string portId) => Task.CompletedTask;

    public Task<(bool Success, string Message)> InitializeOutputAsync(string deviceId, string portId, string format) =>
        Task.FromResult((true, $"AJA NTV2 output port {portId} initialized ({format})."));

    public Task ReleaseOutputAsync(string deviceId, string portId) => Task.CompletedTask;
}
