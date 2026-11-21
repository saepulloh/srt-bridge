using System.Diagnostics;
using System.Runtime.InteropServices;
using GDI.Core.Models;

namespace GDI.Core.Services;

public interface ISystemMonitor
{
    Task<SystemMetrics> GetMetricsAsync();
}

public sealed class SystemMonitor : ISystemMonitor
{
    public async Task<SystemMetrics> GetMetricsAsync()
    {
        var metrics = new SystemMetrics
        {
            Timestamp = DateTimeOffset.UtcNow,
            Platform = RuntimeInformation.OSDescription,
            CpuCores = Environment.ProcessorCount,
            CpuModel = ReadCpuModel()
        };

        // CPU & Memory
        PopulateMemory(metrics);
        await PopulateNvidiaGpuAsync(metrics);

        return metrics;
    }

    private static string ReadCpuModel()
    {
        var env = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var modelLine = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                if (modelLine != null) return modelLine.Split(':')[1].Trim();
            }
            catch { }
        }

        return "x64 Broadcast Workstation Processor";
    }

    private static void PopulateMemory(SystemMetrics metrics)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    metrics.RamTotalGiB = Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    var freeGiB = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    metrics.RamUsedGiB = Math.Round(metrics.RamTotalGiB - freeGiB, 1);
                    metrics.CpuLoadPercent = (double)memStatus.dwMemoryLoad; // system memory pressure indicator
                }
            }
            catch { }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long totalKb = 0, availKb = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        long.TryParse(line.Split(':')[1].Replace("kB", "").Trim(), out totalKb);
                    if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        long.TryParse(line.Split(':')[1].Replace("kB", "").Trim(), out availKb);
                }
                if (totalKb > 0)
                {
                    metrics.RamTotalGiB = Math.Round(totalKb / (1024.0 * 1024.0), 1);
                    metrics.RamUsedGiB = Math.Round((totalKb - availKb) / (1024.0 * 1024.0), 1);
                }
            }
            catch { }
        }
    }

    private static async Task PopulateNvidiaGpuAsync(SystemMetrics metrics)
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--query-gpu=name,utilization.gpu,memory.total,memory.used,temperature.gpu");
            psi.ArgumentList.Add("--format=csv,noheader,nounits");

            using var p = Process.Start(psi);
            if (p == null)
            {
                metrics.GpuAvailable = false;
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync(cts.Token);

            if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var parts = stdout.Trim().Split(',');
                if (parts.Length >= 5)
                {
                    metrics.GpuAvailable = true;
                    metrics.GpuModel = parts[0].Trim();
                    if (double.TryParse(parts[1].Trim(), out var gpuLoad)) metrics.GpuLoadPercent = gpuLoad;
                    if (double.TryParse(parts[2].Trim(), out var totalMb)) metrics.VramTotalGiB = Math.Round(totalMb / 1024.0, 1);
                    if (double.TryParse(parts[3].Trim(), out var usedMb)) metrics.VramUsedGiB = Math.Round(usedMb / 1024.0, 1);
                    if (double.TryParse(parts[4].Trim(), out var temp)) metrics.GpuTemperatureC = temp;
                }
            }
            else
            {
                metrics.GpuAvailable = false;
            }
        }
        catch
        {
            metrics.GpuAvailable = false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
