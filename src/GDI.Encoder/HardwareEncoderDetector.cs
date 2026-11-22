using System.Diagnostics;

namespace GDI.Encoder;

public sealed record CodecCapability(
    bool H264Nvenc,
    bool HevcNvenc,
    bool H264Qsv,
    bool HevcQsv,
    bool H264Amf,
    bool HevcAmf,
    bool Libx264,
    bool Libx265,
    string PreferredH264,
    string PreferredHevc,
    bool H264Vaapi = false,
    bool HevcVaapi = false
);

public sealed class HardwareEncoderDetector
{
    private readonly string _ffmpegPath;

    public HardwareEncoderDetector(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<CodecCapability> DetectAsync()
    {
        var encodersOutput = await RunAsync(_ffmpegPath, ["-hide_banner", "-encoders"], 5000);
        var encoders = encodersOutput.Output + encodersOutput.Error;

        var h264Nvenc = encoders.Contains("h264_nvenc") && await ProbeEncoderAsync("h264_nvenc");
        var hevcNvenc = encoders.Contains("hevc_nvenc") && await ProbeEncoderAsync("hevc_nvenc");
        var h264Qsv = encoders.Contains("h264_qsv") && await ProbeEncoderAsync("h264_qsv");
        var hevcQsv = encoders.Contains("hevc_qsv") && await ProbeEncoderAsync("hevc_qsv");
        var h264Amf = encoders.Contains("h264_amf") && await ProbeEncoderAsync("h264_amf");
        var hevcAmf = encoders.Contains("hevc_amf") && await ProbeEncoderAsync("hevc_amf");
        var h264Vaapi = OperatingSystem.IsLinux() && encoders.Contains("h264_vaapi") && await ProbeEncoderAsync("h264_vaapi");
        var hevcVaapi = OperatingSystem.IsLinux() && encoders.Contains("hevc_vaapi") && await ProbeEncoderAsync("hevc_vaapi");
        var h264Vt = encoders.Contains("h264_videotoolbox") && await ProbeEncoderAsync("h264_videotoolbox");
        var hevcVt = encoders.Contains("hevc_videotoolbox") && await ProbeEncoderAsync("hevc_videotoolbox");
        var libx264 = encoders.Contains("libx264");
        var libx265 = encoders.Contains("libx265");

        var prefH264 = h264Nvenc ? "h264_nvenc" : h264Qsv ? "h264_qsv" : h264Amf ? "h264_amf" : h264Vaapi ? "h264_vaapi" : h264Vt ? "h264_videotoolbox" : "libx264";
        var prefHevc = hevcNvenc ? "hevc_nvenc" : hevcQsv ? "hevc_qsv" : hevcAmf ? "hevc_amf" : hevcVaapi ? "hevc_vaapi" : hevcVt ? "hevc_videotoolbox" : "libx265";

        return new CodecCapability(
            h264Nvenc, hevcNvenc, h264Qsv, hevcQsv, h264Amf, hevcAmf, libx264, libx265, prefH264, prefHevc, h264Vaapi, hevcVaapi
        );
    }

    public async Task<bool> ProbeEncoderAsync(string encoder)
    {
        try
        {
            var psi = new ProcessStartInfo(_ffmpegPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error", "-f", "lavfi",
                "-i", "testsrc2=size=320x240:rate=25", "-frames:v", "1",
                "-c:v", encoder, "-f", "null", "-"
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var p = Process.Start(psi);
            if (p is null) return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string file, string[] args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "Process failed to start");

            var oTask = p.StandardOutput.ReadToEndAsync();
            var eTask = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            await p.WaitForExitAsync(cts.Token);
            return (p.ExitCode, await oTask, await eTask);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
