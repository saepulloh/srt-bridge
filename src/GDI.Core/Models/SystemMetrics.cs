namespace GDI.Core.Models;

public sealed class SystemMetrics
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Platform { get; set; } = "";
    public string CpuModel { get; set; } = "";
    public int CpuCores { get; set; }
    public double CpuLoadPercent { get; set; }
    public double RamTotalGiB { get; set; }
    public double RamUsedGiB { get; set; }
    public double RamUsagePercent => RamTotalGiB > 0 ? (RamUsedGiB / RamTotalGiB) * 100.0 : 0;
    
    public bool GpuAvailable { get; set; }
    public string GpuModel { get; set; } = "None";
    public double GpuLoadPercent { get; set; }
    public double VramTotalGiB { get; set; }
    public double VramUsedGiB { get; set; }
    public double? GpuTemperatureC { get; set; }

    public int ActiveChannels { get; set; }
    public int ActiveSrtConnections { get; set; }
    public int ActiveEncoders { get; set; }
    public int ActiveDecoders { get; set; }

    public double TotalRxThroughputMbps { get; set; }
    public double TotalTxThroughputMbps { get; set; }

    public List<string> Alerts { get; set; } = [];
}
