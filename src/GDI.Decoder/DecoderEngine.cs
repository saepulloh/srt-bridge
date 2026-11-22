using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Services;
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
    private readonly ConcurrentDictionary<Guid, AudioMeterEngine> _audioMeters = new();

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
            metrics.ActiveHardwareBackend = "Auto Hardware Decode (NVDEC / QSV / D3D11VA)";

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
            string pixelFormat = !string.IsNullOrWhiteSpace(channel.PixelFormat) ? channel.PixelFormat : "UYVY";

            ProcessStartInfo psi = new ProcessStartInfo(_ffmpegPath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true, // Stream raw video to DeckLink or In-Memory Multiview
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("info");
            psi.ArgumentList.Add("-stats");
            psi.ArgumentList.Add("-stats_period"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-hwaccel"); psi.ArgumentList.Add("auto"); // Safe hardware decode with CPU fallback
            psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
            psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
            psi.ArgumentList.Add("-analyzeduration"); psi.ArgumentList.Add("100000");
            psi.ArgumentList.Add("-probesize"); psi.ArgumentList.Add("100000");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(srtUrl);

            // Precision Lanczos scaling + BT.709 color matrix conversion to preserve maximum broadcast sharpness
            string sdiPixFmt = pixelFormat.Equals("v210", StringComparison.OrdinalIgnoreCase) ? "v210" : "uyvy422";
            string lanczosScale = $"scale={targetWidth}:{targetHeight}:flags=lanczos+accurate_rnd+full_chroma_int+full_chroma_inp:out_color_matrix=bt709:out_range=tv";

            if (isDecklinkOutput)
            {
                // Complex filter splits video into uncompressed SDI stream for DeckLink and JPEG snapshot for disk/remote API
                psi.ArgumentList.Add("-filter_complex");
                psi.ArgumentList.Add($"[0:v]split=2[raw][prev];[raw]fps={targetFps},{lanczosScale},format={sdiPixFmt}[rawout];[prev]fps=5,scale=960:540:flags=bicubic[prevout]");

                // Output 1: stdout raw video for DeckLink SDI Playout
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[rawout]");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
                psi.ArgumentList.Add("pipe:1");

                // Output 2: JPEG snapshot for Web Remote and API clients
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
                // Multiview / Desktop In-Memory Mode: Stream uncompressed 640x360 @ 30 FPS to pipe:1 for zero-stutter preview
                psi.ArgumentList.Add("-filter_complex");
                psi.ArgumentList.Add($"[0:v]split=2[raw][prev];[raw]fps=30,scale=640:360:flags=bicubic,format=uyvy422[rawout];[prev]fps=5,scale=640:360:flags=bicubic[prevout]");

                // Output 1: In-memory raw video pipe for RealtimeFrameDistributor
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[rawout]");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
                psi.ArgumentList.Add("pipe:1");

                // Output 2: Live preview snapshot for Web Remote API
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[prevout]");
                psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("mjpeg");
                psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("3");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("image2");
                psi.ArgumentList.Add("-update"); psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add(previewFile);
            }

            // Output 3: Real audio metering stream mapped to null sink via astats
            if (channel.IncludeAudio)
            {
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:a:0?");
                psi.ArgumentList.Add("-af");
                psi.ArgumentList.Add("aformat=channel_layouts=stereo,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.1.RMS_level:key=lavfi.astats.2.RMS_level:key=lavfi.astats.1.Peak_level:key=lavfi.astats.2.Peak_level");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("null");
                psi.ArgumentList.Add("-");
            }

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var audioMeter = _audioMeters.GetOrAdd(channel.Id, _ => new AudioMeterEngine());

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
                }
            };

            process.Exited += (_, _) =>
            {
                metrics.State = "STOPPED";
                metrics.AudioLevelLeftDb = -60f;
                metrics.AudioLevelRightDb = -60f;
                metrics.AudioPeakHoldLeftDb = -60f;
                metrics.AudioPeakHoldRightDb = -60f;
                metrics.IsAudioSilent = true;
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

            var cts = new CancellationTokenSource();
            _playoutCts[channel.Id] = cts;
            var rawStream = process.StandardOutput.BaseStream;

            if (isDecklinkOutput)
            {
                _ = Task.Run(async () =>
                {
                    _eventBus.Publish($"[SDI DECODER] Initiating DeckLink SDI scheduled playout on Port {targetPort} ({targetWidth}x{targetHeight} @ {targetFps}p {sdiPixFmt})...");
                    await DeckLinkPlayoutEngine.PlayRawFramesAsync(
                        targetPort, targetWidth, targetHeight, targetFps,
                        rawStream, null, cts.Token,
                        msg => _eventBus.Publish(msg),
                        pixelFormatName: sdiPixFmt,
                        onFrameDecoded: (buf, w, h, fmt) =>
                        {
                            RealtimeFrameDistributor.Instance.PushFrame(channel.Id, w, h, buf, fmt);
                            metrics.PreviewFps = targetFps;
                        },
                        onAudioSamples: pcm =>
                        {
                            var state = audioMeter.ProcessPcm16Stereo(pcm);
                            metrics.AudioLevelLeftDb = state.RmsLeftDb;
                            metrics.AudioLevelRightDb = state.RmsRightDb;
                            metrics.PeakLeftDb = state.PeakLeftDb;
                            metrics.PeakRightDb = state.PeakRightDb;
                            metrics.AudioPeakHoldLeftDb = state.PeakHoldLeftDb;
                            metrics.AudioPeakHoldRightDb = state.PeakHoldRightDb;
                            metrics.IsAudioClippingLeft = state.IsClippingLeft;
                            metrics.IsAudioClippingRight = state.IsClippingRight;
                            metrics.IsAudioSilent = state.IsSilent;
                        }
                    );
                });
            }
            else
            {
                // In-Memory preview worker: reads uncompressed 640x360 frames (460,800 bytes) and pushes directly to WPF
                _ = Task.Run(async () =>
                {
                    int prevW = 640;
                    int prevH = 360;
                    int frameBytes = prevW * prevH * 2; // UYVY
                    byte[] buffer = new byte[frameBytes];

                    try
                    {
                        while (!cts.Token.IsCancellationRequested && !process.HasExited)
                        {
                            int total = 0;
                            while (total < frameBytes && !cts.Token.IsCancellationRequested)
                            {
                                int read = await rawStream.ReadAsync(buffer.AsMemory(total, frameBytes - total), cts.Token);
                                if (read == 0) break;
                                total += read;
                            }

                            if (total < frameBytes) break;

                            RealtimeFrameDistributor.Instance.PushFrame(channel.Id, prevW, prevH, buffer, "UYVY");
                            metrics.PreviewFps = 30.0;
                        }
                    }
                    catch { }
                });
            }

            _processes[channel.Id] = process;
            metrics.State = "RECEIVING";

            var outputDesc = isDecklinkOutput ? $"DeckLink SDI Port {targetPort} ({sdiPixFmt})" : "In-Memory Realtime Multiview";
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
            m.AudioPeakHoldLeftDb = -60f;
            m.AudioPeakHoldRightDb = -60f;
            m.IsAudioSilent = true;
        }
        finally
        {
            gate.Release();
        }
    }
}
