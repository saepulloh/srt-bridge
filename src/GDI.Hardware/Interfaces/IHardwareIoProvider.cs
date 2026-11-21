using GDI.Core.Models;

namespace GDI.Hardware.Interfaces;

public enum HardwareTestStatus
{
    Implemented,
    Compiled,
    Simulated,
    SdkConnected,
    DeviceDetected,
    HardwareTested
}

public interface IDeviceDiscovery
{
    string VendorName { get; }
    Task<IReadOnlyList<IoDevice>> DiscoverDevicesAsync();
}

public interface IDeviceCapabilityProvider
{
    Task<IReadOnlyList<string>> GetSupportedFormatsAsync(string deviceId, string portId);
    Task<bool> ValidateFormatAsync(string deviceId, string portId, string format);
}

public interface IInputCaptureProvider
{
    Task<(bool Success, string Message)> InitializeInputAsync(string deviceId, string portId, string format);
    Task ReleaseInputAsync(string deviceId, string portId);
}

public interface IOutputPlaybackProvider
{
    Task<(bool Success, string Message)> InitializeOutputAsync(string deviceId, string portId, string format);
    Task ReleaseOutputAsync(string deviceId, string portId);
}

public interface IHardwareIoProvider : IDeviceDiscovery, IDeviceCapabilityProvider, IInputCaptureProvider, IOutputPlaybackProvider
{
    HardwareTestStatus VerificationStatus { get; }
}
