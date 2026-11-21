using System.Diagnostics;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Storage;

namespace GDI.ProgramReturn;

public sealed record ProgramReturnStatus(
    bool IsRunning,
    string State,
    string Message,
    int? ProcessId,
    string Role,
    string Endpoint
);

public interface IProgramReturnEngine
{
    ProgramReturnStatus GetStatus();
    Task<(bool Success, string Message)> StartAsync(ProgramReturnConfig config);
    Task StopAsync();
}

public sealed class ProgramReturnEngine : IProgramReturnEngine, IDisposable
{
    private readonly string _ffmpegPath;
    private readonly string _ffplayPath;
    private readonly IConfigurationStore _configStore;
    private readonly ISecretVault _vault;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string _state = "STOPPED";
    private string _message = "Not running";
    private ProgramReturnConfig? _activeConfig;
    private const string ConfigFileName = "program-return.json";

    public ProgramReturnEngine(
        string ffmpegPath,
        string ffplayPath,
        IConfigurationStore configStore,
        ISecretVault vault)
    {
        _ffmpegPath = ffmpegPath;
        _ffplayPath = ffplayPath;
        _configStore = configStore;
        _vault = vault;
    }

    public ProgramReturnStatus GetStatus()
    {
        var running = _process != null && !_process.HasExited;
        var role = _activeConfig?.Role ?? "unknown";
        var endpoint = _activeConfig != null ? $"{_activeConfig.Host}:{_activeConfig.Port}" : "";
        return new ProgramReturnStatus(running, running ? "RUNNING" : _state, _message, running ? _process?.Id : null, role, endpoint);
    }

    public async Task<(bool Success, string Message)> StartAsync(ProgramReturnConfig config)
    {
        await _gate.WaitAsync();
        try
        {
            if (_process != null && !_process.HasExited)
            {
                return (false, "Program Return pipeline is already running.");
            }

            _process?.Dispose();
            _process = null;

            // Secure passphrase handling
            if (!string.IsNullOrWhiteSpace(config.Passphrase))
            {
                _vault.Protect("program_return_passphrase", config.Passphrase);
            }
            else
            {
                config.Passphrase = _vault.Unprotect("program_return_passphrase");
            }

            // Save config with redacted passphrase on disk
            var copy = config.Clone();
            copy.Passphrase = null;
            await _configStore.SaveAsync(ConfigFileName, copy);

            _activeConfig = config;

            var executable = config.Role == "studio-send" ? _ffmpegPath : _ffplayPath;
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = config.Role == "studio-send"
            };

            if (config.Role == "studio-send")
            {
                BuildStudioSenderArgs(psi, config);
            }
            else
            {
                BuildVenueReceiverArgs(psi, config);
            }

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _state = "RUNNING";
                }
            };

            p.Exited += (_, _) =>
            {
                _state = "STOPPED";
                _message = "Program Return process exited.";
            };

            if (!p.Start())
            {
                _state = "ERROR";
                _message = $"Failed to start {executable}";
                return (false, _message);
            }

            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            _process = p;
            _state = "RUNNING";
            _message = config.Role == "studio-send"
                ? $"Transmitting Program Return feed to {config.Host}:{config.Port} via SRT"
                : $"Receiving Studio Program Return feed on UDP port {config.Port}";

            return (true, _message);
        }
        catch (Exception ex)
        {
            _state = "ERROR";
            _message = ex.Message;
            return (false, $"Program Return error: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                _process.Dispose();
                _process = null;
            }
            _state = "STOPPED";
            _message = "Program Return stopped.";
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void BuildStudioSenderArgs(ProcessStartInfo psi, ProgramReturnConfig c)
    {
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("info");
        psi.ArgumentList.Add("-thread_queue_size"); psi.ArgumentList.Add("1024");

        if (c.InputBackend == "dshow")
        {
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("dshow");
            psi.ArgumentList.Add("-framerate"); psi.ArgumentList.Add(c.Fps.ToString());
            psi.ArgumentList.Add("-video_size"); psi.ArgumentList.Add($"{c.Width}x{c.Height}");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(string.IsNullOrWhiteSpace(c.AudioDevice) ? $"video={c.VideoDevice}" : $"video={c.VideoDevice}:audio={c.AudioDevice}");
        }
        else if (c.InputBackend == "v4l2")
        {
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("v4l2");
            psi.ArgumentList.Add("-framerate"); psi.ArgumentList.Add(c.Fps.ToString());
            psi.ArgumentList.Add("-video_size"); psi.ArgumentList.Add($"{c.Width}x{c.Height}");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(c.VideoDevice);
            if (!string.IsNullOrWhiteSpace(c.AudioDevice))
            {
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("alsa");
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(c.AudioDevice);
            }
        }
        else
        {
            // Test pattern fallback
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add($"testsrc2=size={c.Width}x{c.Height}:rate={c.Fps}");
        }

        psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"scale={c.Width}:{c.Height},fps={c.Fps}");

        var enc = c.Encoder switch
        {
            "nvenc" => "h264_nvenc",
            "qsv" => "h264_qsv",
            "amf" => "h264_amf",
            _ => "libx264"
        };

        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add(enc);
        if (enc == "h264_nvenc")
        {
            psi.ArgumentList.Add("-preset"); psi.ArgumentList.Add("p4");
            psi.ArgumentList.Add("-tune"); psi.ArgumentList.Add("ll");
            psi.ArgumentList.Add("-rc"); psi.ArgumentList.Add("cbr");
        }
        else
        {
            psi.ArgumentList.Add("-preset"); psi.ArgumentList.Add("veryfast");
            psi.ArgumentList.Add("-tune"); psi.ArgumentList.Add("zerolatency");
        }

        psi.ArgumentList.Add("-b:v"); psi.ArgumentList.Add($"{c.BitrateKbps}k");
        psi.ArgumentList.Add("-maxrate"); psi.ArgumentList.Add($"{c.BitrateKbps}k");
        psi.ArgumentList.Add("-bufsize"); psi.ArgumentList.Add($"{c.BitrateKbps * 2}k");
        psi.ArgumentList.Add("-g"); psi.ArgumentList.Add((c.Fps * 2).ToString());
        psi.ArgumentList.Add("-bf"); psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");

        if (!string.IsNullOrWhiteSpace(c.AudioDevice))
        {
            psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("128k");
            psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add("48000");
        }
        else
        {
            psi.ArgumentList.Add("-an");
        }

        // SRT Caller URL
        var q = $"mode=caller&transtype=live&latency={c.LatencyMs}";
        if (!string.IsNullOrWhiteSpace(c.Passphrase))
        {
            q += $"&passphrase={Uri.EscapeDataString(c.Passphrase)}&pbkeylen={c.PbKeyLength}";
        }

        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("mpegts");
        psi.ArgumentList.Add($"srt://{c.Host}:{c.Port}?{q}");
    }

    private static void BuildVenueReceiverArgs(ProcessStartInfo psi, ProgramReturnConfig c)
    {
        var q = $"mode=listener&transtype=live&latency={c.LatencyMs}";
        if (!string.IsNullOrWhiteSpace(c.Passphrase))
        {
            q += $"&passphrase={Uri.EscapeDataString(c.Passphrase)}&pbkeylen={c.PbKeyLength}";
        }
        var url = $"srt://0.0.0.0:{c.Port}?{q}";

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("warning");
        psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
        psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
        psi.ArgumentList.Add("-framedrop");

        if (c.Fullscreen)
        {
            psi.ArgumentList.Add("-fs");
        }

        psi.ArgumentList.Add("-window_title");
        psi.ArgumentList.Add($"GDI PROGRAM RETURN - VENUE MONITOR ({c.Width}x{c.Height})");
        psi.ArgumentList.Add(url);
    }

    public void Dispose()
    {
        _process?.Dispose();
        _gate.Dispose();
    }
}
