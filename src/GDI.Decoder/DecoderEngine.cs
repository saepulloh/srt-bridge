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
    private static bool? _cudaDecoderAvailable = null;
    private static readonly SemaphoreSlim _cudaProbeLock = new(1, 1);

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

    private async Task<bool> IsCudaDecoderAvailableAsync()
    {
        if (_cudaDecoderAvailable.HasValue) return _cudaDecoderAvailable.Value;
        await _cudaProbeLock.WaitAsync();
        try
        {
            if (_cudaDecoderAvailable.HasValue) return _cudaDecoderAvailable.Value;
            var psi = new ProcessStartInfo(_ffmpegPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-hwaccel"); psi.ArgumentList.Add("cuda");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc2=size=320x240:rate=25");
            psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");

            using var p = Process.Start(psi);
            if (p is null)
            {
                _cudaDecoderAvailable = false;
                return false;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await p.WaitForExitAsync(cts.Token);
            _cudaDecoderAvailable = (p.ExitCode == 0);
            return _cudaDecoderAvailable.Value;
        }
        catch
        {
            _cudaDecoderAvailable = false;
            return false;
        }
        finally
        {
            _cudaProbeLock.Release();
        }
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

            bool hasCuda = await IsCudaDecoderAvailableAsync();
            metrics.ActiveHardwareBackend = hasCuda
                ? "NVIDIA NVDEC (CUDA Accelerated)"
                : "Auto Hardware Decode (NVDEC / QSV / D3D11VA)";

            var previewDir = GdiEnvironment.GetPreviewsDirectory();
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
            string sdiPixFmt = pixelFormat.Equals("v210", StringComparison.OrdinalIgnoreCase) ? "v210" : "uyvy422";

            int outW;
            int outH;
            int outFps;

            if (isDecklinkOutput)
            {
                // SDI Playout Path: Strictly maintains native broadcast target geometry (e.g. 1920x1080 @ 50p)
                outW = targetWidth;
                outH = targetHeight;
                outFps = targetFps;
            }
            else
            {
                // Multiview / Desktop In-Memory Output: Configurable tiered resolution
                // Standard: 960x540 @ 30 FPS (Default: optimal bandwidth and CPU for 16-channel multiview)
                // HighQuality: 1280x720 @ 30 FPS (Enhanced definition for quad/dual layouts)
                // FullNative / FullScreen: 1920x1080 @ 50 FPS (Source native resolution for single view)
                string q = !string.IsNullOrWhiteSpace(channel.MultiviewQuality)
                    ? channel.MultiviewQuality
                    : "Standard";

                switch (q.ToLowerInvariant())
                {
                    case "highquality":
                    case "720p":
                        outW = 1280;
                        outH = 720;
                        outFps = 30;
                        break;

                    case "fullnative":
                    case "fullscreen":
                    case "1080p":
                        outW = targetWidth;
                        outH = targetHeight;
                        outFps = targetFps;
                        break;

                    case "standard":
                    default:
                        outW = 960;
                        outH = 540;
                        outFps = 30;
                        break;
                }
            }

            // Section 3: Dynamic stride and frame buffer size calculation
            // For UYVY422: FrameBytes = Stride * Height
            bool isV210 = sdiPixFmt.Equals("v210", StringComparison.OrdinalIgnoreCase);
            int bytesPerPixel = isV210 ? 3 : 2;
            int stride = isV210 ? ((outW + 47) / 48) * 128 : outW * 2;
            int frameBytes = stride * outH;

            // Section 7: Diagnostics & Telemetry
            string scalingStatus = (targetWidth == outW && targetHeight == outH)
                ? "DISABLED (1:1 Native Passthrough)"
                : $"ENABLED (Lanczos {outW}x{outH})";
            metrics.SourceResolution = $"{targetWidth}x{targetHeight} @ {targetFps} FPS";
            metrics.DecoderResolution = $"{targetWidth}x{targetHeight} @ {targetFps} FPS";
            metrics.MultiviewResolution = $"{outW}x{outH} @ {outFps} FPS";
            metrics.ScalingStatus = scalingStatus;
            metrics.FrameBufferSize = frameBytes;
            metrics.FrameStride = stride;

            _eventBus.Publish($"[DECODER CONFIG] Channel '{channel.Name}' (ID {channel.Id}):\n" +
                $"  SOURCE: {metrics.SourceResolution}\n" +
                $"  DECODE: {metrics.DecoderResolution}\n" +
                $"  MULTIVIEW: {metrics.MultiviewResolution}\n" +
                $"  SCALING: {scalingStatus}\n" +
                $"  FORMAT: {sdiPixFmt.ToUpperInvariant()}\n" +
                $"  BUFFER: {frameBytes:N0} bytes (Stride: {stride})");

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

            // Hardware decoding: prioritize dedicated NVIDIA RTX 4060 NVDEC via CUDA with safe auto/CPU fallback
            if (hasCuda)
            {
                psi.ArgumentList.Add("-hwaccel"); psi.ArgumentList.Add("cuda");
            }
            else
            {
                psi.ArgumentList.Add("-hwaccel"); psi.ArgumentList.Add("auto");
            }

            psi.ArgumentList.Add("-fflags"); psi.ArgumentList.Add("nobuffer");
            psi.ArgumentList.Add("-flags"); psi.ArgumentList.Add("low_delay");
            psi.ArgumentList.Add("-analyzeduration"); psi.ArgumentList.Add("100000");
            psi.ArgumentList.Add("-probesize"); psi.ArgumentList.Add("100000");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(srtUrl);

            // Section 4: Filter Graph Optimization
            // If input 1920x1080 == outW x outH, eliminate scale filter to prevent redundant resampling and preserve native sharpness!
            // If target differs (e.g. 960x540 or 1280x720), apply precision Lanczos scaling.
            var rawFilters = new List<string>();
            if (outFps > 0)
            {
                rawFilters.Add($"fps={outFps}");
            }

            bool isNativePassthrough = (targetWidth == outW && targetHeight == outH);
            if (!isNativePassthrough)
            {
                rawFilters.Add($"scale={outW}:{outH}:flags=lanczos+accurate_rnd+full_chroma_int+full_chroma_inp:out_color_matrix=bt709:out_range=tv");
            }

            rawFilters.Add($"format={sdiPixFmt}");
            string rawFilterStr = string.Join(",", rawFilters);

            // Unified filter complex:
            // [rawout]: Uncompressed broadcast feed streamed to pipe:1 for SDI playout and Desktop Multiview
            // [prevout]: Isolated 960x540 @ 5fps snapshot for disk/remote API
            psi.ArgumentList.Add("-filter_complex");
            psi.ArgumentList.Add($"[0:v]split=2[raw][prev];[raw]{rawFilterStr}[rawout];[prev]fps=5,scale=960:540:flags=bicubic[prevout]");

            // Output 1: stdout raw video for DeckLink SDI Playout or In-Memory Multiview
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

            // Output 3: Real audio metering stream mapped to null sink via astats
            if (channel.IncludeAudio)
            {
                psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:a:0?");
                psi.ArgumentList.Add("-af");
                psi.ArgumentList.Add("aformat=channel_layouts=stereo,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.1.RMS_level,ametadata=print:key=lavfi.astats.2.RMS_level,ametadata=print:key=lavfi.astats.1.Peak_level,ametadata=print:key=lavfi.astats.2.Peak_level");
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
                            RealtimeFrameDistributor.Instance.PushFrame(channel.Id, w, h, buf, fmt, stride);
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
                // In-Memory preview worker: reads uncompressed frames at target multiview resolution and pushes to WPF Multiview
                _ = Task.Run(async () =>
                {
                    // Double-buffering to eliminate 200MB/s LOH GC allocations and prevent memory tearing
                    byte[][] buffers = [new byte[frameBytes], new byte[frameBytes]];
                    int bufIdx = 0;

                    try
                    {
                        while (!cts.Token.IsCancellationRequested && !process.HasExited)
                        {
                            byte[] currentBuf = buffers[bufIdx];
                            int total = 0;
                            while (total < frameBytes && !cts.Token.IsCancellationRequested)
                            {
                                int read = await rawStream.ReadAsync(currentBuf.AsMemory(total, frameBytes - total), cts.Token);
                                if (read == 0) break;
                                total += read;
                            }

                            if (total < frameBytes) break;

                            RealtimeFrameDistributor.Instance.PushFrame(channel.Id, outW, outH, currentBuf, sdiPixFmt, stride);
                            metrics.PreviewFps = outFps;
                            bufIdx ^= 1;
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
