using GDI.Core.Models;

namespace GDI.Desktop.ViewModels;

public sealed class SystemMonitorViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private double _cpuLoad;
    public double CpuLoad { get => _cpuLoad; set => SetProperty(ref _cpuLoad, value); }

    private string _cpuName = "Intel / AMD Processor";
    public string CpuName { get => _cpuName; set => SetProperty(ref _cpuName, value); }

    private double _ramLoad;
    public double RamLoad { get => _ramLoad; set => SetProperty(ref _ramLoad, value); }

    private string _ramText = "0 / 0 GB";
    public string RamText { get => _ramText; set => SetProperty(ref _ramText, value); }

    private string _gpuModel = "GPU Not Detected";
    public string GpuModel { get => _gpuModel; set => SetProperty(ref _gpuModel, value); }

    private string _vramText = "0 / 0 GiB";
    public string VramText { get => _vramText; set => SetProperty(ref _vramText, value); }

    private string _gpuTempText = "N/A";
    public string GpuTempText { get => _gpuTempText; set => SetProperty(ref _gpuTempText, value); }

    private int _encoderSessions;
    public int EncoderSessions { get => _encoderSessions; set => SetProperty(ref _encoderSessions, value); }

    private int _decoderSessions;
    public int DecoderSessions { get => _decoderSessions; set => SetProperty(ref _decoderSessions, value); }

    private string _nvencStatus = "NVENC / QSV Hardware Acceleration Ready";
    public string NvencStatus { get => _nvencStatus; set => SetProperty(ref _nvencStatus, value); }

    public SystemMonitorViewModel(MainViewModel main)
    {
        _main = main;
    }

    public void UpdateTelemetry(SystemMetrics sys, IReadOnlyList<ChannelConfig> all)
    {
        CpuLoad = sys.CpuLoadPercent;
        CpuName = $"{sys.CpuModel} ({sys.CpuCores} Logical Cores)";
        RamLoad = sys.RamUsagePercent;
        RamText = $"{sys.RamUsedGiB} / {sys.RamTotalGiB} GiB";

        if (sys.GpuAvailable)
        {
            GpuModel = sys.GpuModel;
            VramText = $"{sys.VramUsedGiB} / {sys.VramTotalGiB} GiB";
            GpuTempText = sys.GpuTemperatureC.HasValue ? $"{sys.GpuTemperatureC.Value} °C" : "N/A";
            NvencStatus = "Hardware Acceleration Active (DirectX / NVENC / QSV)";
        }
        else
        {
            GpuModel = "Software Graphics / CPU Mode";
            VramText = "System Shared Memory";
            GpuTempText = "N/A";
            NvencStatus = "CPU Software Encoding (libx264/libx265)";
        }

        EncoderSessions = all.Count(c => c.Direction == ChannelDirection.Encoder && _main.ChannelManager.IsRunning(c.Id));
        DecoderSessions = all.Count(c => c.Direction == ChannelDirection.Decoder && _main.ChannelManager.IsRunning(c.Id));
    }
}
