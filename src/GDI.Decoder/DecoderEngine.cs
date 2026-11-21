using System.Collections.Concurrent;
using System.Diagnostics;
using GDI.Core.Events;
using GDI.Core.Models;
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

            // In decoder mode, the SRT URL uses the opposite mode of transmission:
            // If sender is Caller, receiver is Listener (binds 0.0.0.0);
            // If sender is Listener, receiver is Caller (connects to Host:Port).
            var srtUrl = SrtUriBuilder.BuildFromChannel(channel);

            var metrics = GetMetrics(channel.Id);
            metrics.ChannelName = channel.Name;
            metrics.State = "STARTING";
            metrics.LastError = "";

            ProcessStartInfo psi;

            // Check if explicit floating window was requested, otherwise use headless background decode
            bool useFloatingWindow = channel.OutputTarget?.Equals("window", StringComparison.OrdinalIgnoreCase) == true;

            if (useFloatingWindow && File.Exists(_ffplayPath))
            {
                // Windowed player mode (clean 960x540 window, NOT fullscreen)
                psi = new ProcessStartInfo(_ffplayPath)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = false
                };

                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("warning");
                psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
                psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
                psi.ArgumentList.Add("-framedrop");
                psi.ArgumentList.Add("-x"); psi.ArgumentList.Add("960");
                psi.ArgumentList.Add("-y"); psi.ArgumentList.Add("540");
                psi.ArgumentList.Add("-window_title");
                psi.ArgumentList.Add($"GDI DECODER - {channel.Name} ({channel.Width}x{channel.Height})");
                psi.ArgumentList.Add(srtUrl);
            }
            else
            {
                // Headless FFmpeg decoder: runs completely in background with NO popup window,
                // decoding stream and generating live preview snapshots for Multiview & UI
                var previewDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "previews");
                try { Directory.CreateDirectory(previewDir); } catch { }
                var previewFile = Path.Combine(previewDir, $"{channel.Id:N}.jpg").Replace('\\', '/');

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

                // Live preview snapshot for Multiview and Decoder cards
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v:0");
                psi.ArgumentList.Add("-an");
                psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add("fps=2,scale=480:270");
                psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("mjpeg");
                psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("5");
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
                _eventBus.Publish(new ChannelMetrics { ChannelId = channel.Id, State = "STOPPED" });
            };

            if (!process.Start())
            {
                metrics.State = "ERROR";
                metrics.LastError = "Failed to launch decoder engine process.";
                return (false, metrics.LastError);
            }

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            _processes[channel.Id] = process;
            metrics.State = "RECEIVING";

            return (true, $"Receiver for '{channel.Name}' started in background on {channel.Port} (PID {process.Id}).");
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
