using System.Globalization;
using System.Text.RegularExpressions;
using GDI.Core.Models;

namespace GDI.SRT.Engine;

public static class SrtMetricsParser
{
    private static readonly Regex FpsRegex = new(@"fps=\s*([0-9\.]+)", RegexOptions.Compiled);
    private static readonly Regex BitrateRegex = new(@"bitrate=\s*([0-9\.]+)kbits/s", RegexOptions.Compiled);
    private static readonly Regex FrameRegex = new(@"frame=\s*([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex DropRegex = new(@"drop=\s*([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex SpeedRegex = new(@"speed=\s*([0-9\.]+)x", RegexOptions.Compiled);

    // Audio metering regexes for FFmpeg astats / ametadata
    private static readonly Regex Rms1Regex = new(@"astats\.1\.RMS_level=([0-9\.\-]+|-?inf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Rms2Regex = new(@"astats\.2\.RMS_level=([0-9\.\-]+|-?inf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Peak1Regex = new(@"astats\.1\.Peak_level=([0-9\.\-]+|-?inf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Peak2Regex = new(@"astats\.2\.Peak_level=([0-9\.\-]+|-?inf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Hardware decoder/encoder detection regexes
    private static readonly Regex HwaccelRegex = new(@"hwaccel\s+(\w+)|Selected\s+hwaccel\s+(\w+)|(cuvid|nvdec|qsv|d3d11va|videotoolbox|amf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void ParseProgressLine(string line, ChannelMetrics metrics)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var fpsMatch = FpsRegex.Match(line);
        if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fps))
        {
            metrics.CurrentFps = fps;
            metrics.DecoderFps = fps;
        }

        var bitrateMatch = BitrateRegex.Match(line);
        if (bitrateMatch.Success && double.TryParse(bitrateMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bitrate))
        {
            metrics.CurrentBitrateKbps = bitrate;
        }

        var frameMatch = FrameRegex.Match(line);
        if (frameMatch.Success && long.TryParse(frameMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var frame))
        {
            metrics.TotalFrames = frame;
        }

        var dropMatch = DropRegex.Match(line);
        if (dropMatch.Success && long.TryParse(dropMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var drop))
        {
            metrics.DroppedFrames = drop;
        }

        // Parse Real Audio Metering Levels from astats
        var r1 = Rms1Regex.Match(line);
        if (r1.Success)
        {
            float rmsL = float.TryParse(r1.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : -60f;
            if (float.IsNegativeInfinity(rmsL) || float.IsNaN(rmsL)) rmsL = -60f;
            metrics.RmsLeftDb = Math.Clamp(rmsL, -60f, 0f);
            metrics.AudioLevelLeftDb = metrics.RmsLeftDb;
        }

        var r2 = Rms2Regex.Match(line);
        if (r2.Success)
        {
            float rmsR = float.TryParse(r2.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : -60f;
            if (float.IsNegativeInfinity(rmsR) || float.IsNaN(rmsR)) rmsR = -60f;
            metrics.RmsRightDb = Math.Clamp(rmsR, -60f, 0f);
            metrics.AudioLevelRightDb = metrics.RmsRightDb;
        }

        var p1 = Peak1Regex.Match(line);
        if (p1.Success)
        {
            float peakL = float.TryParse(p1.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : -60f;
            if (float.IsNegativeInfinity(peakL) || float.IsNaN(peakL)) peakL = -60f;
            peakL = Math.Clamp(peakL, -60f, 0f);
            metrics.PeakLeftDb = peakL;
            metrics.IsAudioClippingLeft = peakL >= -0.1f;
            if (peakL > metrics.AudioPeakHoldLeftDb)
            {
                metrics.AudioPeakHoldLeftDb = peakL;
            }
        }

        var p2 = Peak2Regex.Match(line);
        if (p2.Success)
        {
            float peakR = float.TryParse(p2.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : -60f;
            if (float.IsNegativeInfinity(peakR) || float.IsNaN(peakR)) peakR = -60f;
            peakR = Math.Clamp(peakR, -60f, 0f);
            metrics.PeakRightDb = peakR;
            metrics.IsAudioClippingRight = peakR >= -0.1f;
            if (peakR > metrics.AudioPeakHoldRightDb)
            {
                metrics.AudioPeakHoldRightDb = peakR;
            }
        }

        if (metrics.RmsLeftDb <= -58f && metrics.RmsRightDb <= -58f)
        {
            metrics.IsAudioSilent = true;
        }
        else if (metrics.RmsLeftDb > -58f || metrics.RmsRightDb > -58f)
        {
            metrics.IsAudioSilent = false;
        }

        // Parse Active Hardware Backend
        var hwMatch = HwaccelRegex.Match(line);
        if (hwMatch.Success)
        {
            var matchStr = hwMatch.Value.ToLowerInvariant();
            if (matchStr.Contains("cuvid") || matchStr.Contains("nvdec") || matchStr.Contains("cuda"))
            {
                metrics.ActiveHardwareBackend = "NVIDIA NVDEC (CUVID Hardware Accelerated)";
            }
            else if (matchStr.Contains("qsv"))
            {
                metrics.ActiveHardwareBackend = "Intel Quick Sync (QSV Hardware Accelerated)";
            }
            else if (matchStr.Contains("d3d11va"))
            {
                metrics.ActiveHardwareBackend = "Direct3D11 VA (Hardware Accelerated)";
            }
            else if (matchStr.Contains("amf"))
            {
                metrics.ActiveHardwareBackend = "AMD AMF (Hardware Accelerated)";
            }
        }

        metrics.LastUpdated = DateTimeOffset.UtcNow;
    }
}
