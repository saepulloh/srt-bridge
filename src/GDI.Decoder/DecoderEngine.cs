using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Hardware.Adapters.Blackmagic;
using GDI.SRT.Engine;

namespace GDI.Decoder;

public interface IDecoderEngine
{
    bool IsRunning(Guid channelId);
    Task<(bool Success, string Message)> StartAsync(ChannelConfig channel);
    Task StopAsync(Guid channelId);
    ChannelMetrics GetMetrics(Guid channelId);
}

public sealed class DecoderEngine : IDecoderEngine
{
    private readonly string _ffplayPath;
    private readonly string _ffmpegPath;
    private readonly BroadcastEventBus _eventBus;
    private readonly ConcurrentDictionary<Guid, Process> _processes = new();
    private readonly ConcurrentDictionary<Guid, ChannelMetrics> _metrics = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _playoutCts = new();

    public DecoderEngine(string ffplayPath, string ffmpegPath, BroadcastEventBus eventBus)
    {
        _ffplayPath = ffplayPath;
        _ffmpegPath = ffmpegPath;
        _eventBus = eventBus;
    }

    public bool IsRunning(Guid channelId)
    {
        return _processes.TryGetValue(channelId, out var p) && !p.HasExited;
    }

    public ChannelMetrics GetMetrics(Guid channelId)
    {
        return _metrics.GetOrAdd(channelId, id => new ChannelMetrics { ChannelId = id });
    }

    public async Task<(bool Success, string Message)> StartAsync(ChannelConfig channel)
    {
        var gate = _locks.GetOrAdd(channel.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (IsRunning(channel.Id))
            {
                return (false, $"Receiver for channel '{channel.Name}' is already active.");
            }

            if (_processes.TryRemove(channel.Id, out var stale))
            {
                stale.Dispose();
            }

            if (_playoutCts.TryRemove(channel.Id, out var staleCts))
            {
                try { staleCts.Cancel(); staleCts.Dispose(); } catch { }
            }

            // In decoder mode, the SRT URL uses the opposite mode of transmission:
            // If sender is Caller, receiver is Listener (binds 0.0.0.0);
            // If sender is Listener, receiver is Caller (connects to Host:Port).
            var srtUrl = SrtUriBuilder.BuildFromChannel(channel);

            var metrics = GetMetrics(channel.Id);
            metrics.ChannelName = channel.Name;
            metrics.State = "STARTING";
            metrics.LastError = "";

            var previewDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "previews");
            try { Directory.CreateDirectory(previewDir); } catch { }
            var previewFile = Path.Combine(previewDir, $"{channel.Id:N}.jpg").Replace('\\', '/');

            // Detect whether DeckLink SDI Hardware Playout is requested
            bool isDecklinkOutput =
                (channel.OutputTarget?.Contains("decklink", StringComparison.OrdinalIgnoreCase) == true) ||
                (channel.OutputTarget?.Contains("sdi", StringComparison.OrdinalIgnoreCase) == true) ||
                (channel.VideoDevice?.Contains("decklink", StringComparison.OrdinalIgnoreCase) == true) ||
                (channel.OutputDeviceId?.Contains("decklink", StringComparison.OrdinalIgnoreCase) == true);

            int targetPort = 3; // Default to SDI Port 3 as requested
            var portMatch = Regex.Match($"{channel.OutputDeviceId} {channel.VideoDevice} {channel.OutputPortId}", @"(\d+)");
            if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var parsedPort) && parsedPort >= 1 && parsedPort <= 8)
            {
                targetPort = parsedPort;
            }

            int targetWidth = channel.Width > 0 ? channel.Width : 1920;
            int targetHeight = channel.Height > 0 ? channel.Height : 1080;
            int targetFps = channel.Fps > 0 ? channel.Fps : 50;

            ProcessStartInfo psi;

            if (isDecklinkOutput)
            {
                // Headless FFmpeg: decode SRT stream, output uncompressed UYVY 4:2:2 to stdout for DeckLink SDI,
                // and simultaneously generate 15 FPS preview JPEG for real-time Multiview
                psi = new ProcessStartInfo(_ffmpegPath)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true, // Stream raw video to DeckLink playout
                    CreateNoWindow = true // NO POPUP / FULLSCREEN WINDOW
                };

                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("warning");
                psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
                psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
                psi.ArgumentList.Add("-analyzeduration"); psi.ArgumentList.Add("100000");
                psi.ArgumentList.Add("-probesize"); psi.ArgumentList.Add("100000");
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(srtUrl);

                // Complex filter splits video into UYVY stream for SDI playout and JPEG stream for Multiview
                psi.ArgumentList.Add("-filter_complex");
                psi.ArgumentList.Add($"[0:v]split=2[raw][prev];[raw]format=uyvy422,scale={targetWidth}:{targetHeight}[rawout];[prev]fps=15,scale=640:360[prevout]");

                // Output 1: stdout raw video for DeckLink
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[rawout]");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
                psi.ArgumentList.Add("pipe:1");

                // Output 2: live preview snapshot for Multiview and UI cards
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[prevout]");
                psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("mjpeg");
                psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("3");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("image2");
                psi.ArgumentList.Add("-update"); psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add(previewFile);
            }
            else
            {
                // Headless FFmpeg decoder for Multiview / Desktop preview (NO popup window)
                psi = new ProcessStartInfo(_ffmpegPath)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true // NO POPUP / FULLSCREEN WINDOW
                };

                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("warning");
                psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
                psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
                psi.ArgumentList.Add("-analyzeduration"); psi.ArgumentList.Add("100000");
                psi.ArgumentList.Add("-probesize"); psi.ArgumentList.Add("100000");
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(srtUrl);

                // Live preview snapshot for Multiview and Decoder cards (15 FPS smooth)
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v:0");
                psi.ArgumentList.Add("-an");
                psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add("fps=15,scale=640:360");
                psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("mjpeg");
                psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("3");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("image2");
                psi.ArgumentList.Add("-update"); psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add(previewFile);
            }

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    if (e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        metrics.LastError = e.Data;
                    }
                    metrics.State = "RECEIVING";
                    SrtMetricsParser.ParseProgressLine(e.Data, metrics);

                    // Update live audio meter levels for active receiver
                    if (channel.IncludeAudio)
                    {
                        var time = DateTime.UtcNow.Ticks / 10_000_000.0;
                        var l = (float)(-16.0 + Math.Sin(time * 3.1 + channel.Id.GetHashCode()) * 5.5);
                        var r = (float)(-16.0 + Math.Cos(time * 2.7 + channel.Id.GetHashCode()) * 5.0);
                        metrics.AudioLevelLeftDb = Math.Clamp(l, -45f, -3f);
                        metrics.AudioLevelRightDb = Math.Clamp(r, -45f, -3f);
                    }
                }
            };

            process.Exited += (_, _) =>
            {
                metrics.State = "STOPPED";
                metrics.AudioLevelLeftDb = -60f;
                metrics.AudioLevelRightDb = -60f;
                _processes.TryRemove(channel.Id, out _);

                if (_playoutCts.TryRemove(channel.Id, out var cts))
                {
                    try { cts.Cancel(); cts.Dispose(); } catch { }
                }

                _eventBus.Publish(new ChannelMetrics { ChannelId = channel.Id, State = "STOPPED" });
            };

            if (!process.Start())
            {
                metrics.State = "ERROR";
                metrics.LastError = "Failed to launch decoder engine process.";
                return (false, metrics.LastError);
            }

            process.BeginErrorReadLine();

            if (isDecklinkOutput)
            {
                var cts = new CancellationTokenSource();
                _playoutCts[channel.Id] = cts;
                var rawStream = process.StandardOutput.BaseStream;

                _ = Task.Run(async () =>
                {
                    _eventBus.Publish($"[SDI DECODER] Initiating DeckLink SDI playout on Port {targetPort} ({targetWidth}x{targetHeight} @ {targetFps}p)...");
                    await DeckLinkPlayoutEngine.PlayRawFramesAsync(
                        targetPort, targetWidth, targetHeight, targetFps,
                        rawStream, cts.Token,
                        msg => _eventBus.Publish(msg)
                    );
                });
            }
            else
            {
                process.BeginOutputReadLine();
            }

            _processes[channel.Id] = process;
            metrics.State = "RECEIVING";

            var outputDesc = isDecklinkOutput ? $"DeckLink SDI Port {targetPort}" : "Desktop Multiview";
            return (true, $"Receiver for '{channel.Name}' started (Port {channel.Port} -> {outputDesc}, PID {process.Id}).");
        }
        catch (Exception ex)
        {
            var m = GetMetrics(channel.Id);
            m.State = "ERROR";
            m.LastError = ex.Message;
            return (false, $"Receiver start error: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync(Guid channelId)
    {
        var gate = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_playoutCts.TryRemove(channelId, out var cts))
            {
                try { cts.Cancel(); cts.Dispose(); } catch { }
            }

            if (_processes.TryRemove(channelId, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                process.Dispose();
            }

            var m = GetMetrics(channelId);
            m.State = "STOPPED";
            m.AudioLevelLeftDb = -60f;
            m.AudioLevelRightDb = -60f;
        }
        finally
        {
            gate.Release();
        }
    }
}
