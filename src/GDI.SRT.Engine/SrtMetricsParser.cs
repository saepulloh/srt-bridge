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

    public static void ParseProgressLine(string line, ChannelMetrics metrics)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var fpsMatch = FpsRegex.Match(line);
        if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fps))
        {
            metrics.CurrentFps = fps;
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

        metrics.LastUpdated = DateTimeOffset.UtcNow;
    }
}
