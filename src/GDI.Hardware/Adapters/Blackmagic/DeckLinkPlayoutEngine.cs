using System.Runtime.InteropServices;
using GDI.Core.Models;

namespace GDI.Hardware.Adapters.Blackmagic;

[ComImport, Guid("50FB36CD-3063-4B73-BDBB-958087F2D8BA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkIterator
{
    [PreserveSig]
    int Next(out IntPtr deckLinkInstance);
}

[ComImport, Guid("C418FBDD-0587-48ED-8FE5-640F0A14AF91"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLink
{
    [PreserveSig]
    int GetModelName([MarshalAs(UnmanagedType.BStr)] out string modelName);
    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.BStr)] out string displayName);
}

[ComImport, Guid("6502091C-615F-4F51-BAF6-45C4256DD5B0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkVideoFrame
{
    [PreserveSig] int GetWidth();
    [PreserveSig] int GetHeight();
    [PreserveSig] int GetRowBytes();
    [PreserveSig] uint GetPixelFormat();
    [PreserveSig] uint GetFlags();
    [PreserveSig] int GetTimecode(uint format, out IntPtr timecode);
    [PreserveSig] int GetAncillaryData(out IntPtr ancillary);
}

[ComImport, Guid("CF9EB134-0374-4C5B-95FA-1EC14819FF62"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkMutableVideoFrame : IDeckLinkVideoFrame
{
    [PreserveSig] new int GetWidth();
    [PreserveSig] new int GetHeight();
    [PreserveSig] new int GetRowBytes();
    [PreserveSig] new uint GetPixelFormat();
    [PreserveSig] new uint GetFlags();
    [PreserveSig] new int GetTimecode(uint format, out IntPtr timecode);
    [PreserveSig] new int GetAncillaryData(out IntPtr ancillary);

    [PreserveSig] int SetFlags(uint flags);
    [PreserveSig] int SetTimecode(uint format, IntPtr timecode);
    [PreserveSig] int SetTimecodeFromComponents(uint format, byte hours, byte minutes, byte seconds, byte frames, uint flags);
    [PreserveSig] int SetAncillaryData(IntPtr ancillary);
    [PreserveSig] int SetTimecodeUserBits(uint format, uint userBits);
    [PreserveSig] int SetInterfaceProvider(IntPtr iface, IntPtr provider);
}

[ComImport, Guid("CCB4B64A-5C86-4E02-B778-885D352709FE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkVideoBuffer
{
    [PreserveSig] int GetBytes(out IntPtr buffer);
    [PreserveSig] int StartAccess(uint accessMode);
    [PreserveSig] int EndAccess();
}

[ComImport, Guid("20AA5225-1958-47CB-820B-80A7432180AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkVideoOutputCallback
{
    [PreserveSig] int ScheduledFrameCompleted(IDeckLinkVideoFrame completedFrame, uint result);
    [PreserveSig] int ScheduledPlaybackHasStopped();
}

[ComImport, Guid("1A8077F1-9FE2-4533-8147-2294305E253F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeckLinkOutput
{
    [PreserveSig] int DoesSupportVideoMode(long connection, uint displayMode, uint pixelFormat, uint conversionMode, uint flags, out uint resultDisplayMode, out int supported);
    [PreserveSig] int GetDisplayMode(uint displayMode, out IntPtr resultDisplayMode);
    [PreserveSig] int GetDisplayModeIterator(out IntPtr iterator);
    [PreserveSig] int SetScreenPreviewCallback(IntPtr previewCallback);
    [PreserveSig] int EnableVideoOutput(uint displayMode, uint outputFlags);
    [PreserveSig] int DisableVideoOutput();
    [PreserveSig] int CreateVideoFrame(int width, int height, int rowBytes, uint pixelFormat, uint flags, out IDeckLinkMutableVideoFrame outFrame);
    [PreserveSig] int CreateVideoFrameWithBuffer(int width, int height, int rowBytes, uint pixelFormat, uint flags, IntPtr buffer, out IDeckLinkMutableVideoFrame outFrame);
    [PreserveSig] int RowBytesForPixelFormat(uint pixelFormat, int width, out int rowBytes);
    [PreserveSig] int CreateAncillaryData(uint pixelFormat, out IntPtr outBuffer);
    [PreserveSig] int DisplayVideoFrameSync(IDeckLinkVideoFrame theFrame);

    // Scheduled playback methods
    [PreserveSig] int ScheduleVideoFrame(IDeckLinkVideoFrame theFrame, long displayTime, long displayDuration, long timeScale);
    [PreserveSig] int SetScheduledFrameCompletionCallback(IDeckLinkVideoOutputCallback theCallback);
    [PreserveSig] int GetBufferedVideoFrameCount(out uint bufferedFrameCount);

    // Audio output methods
    [PreserveSig] int EnableAudioOutput(uint sampleRate, uint sampleType, uint channelCount, uint streamType);
    [PreserveSig] int DisableAudioOutput();
    [PreserveSig] int WriteAudioSamplesSync(IntPtr buffer, uint sampleFrameCount, out uint sampleFramesWritten);
    [PreserveSig] int BeginAudioPreroll();
    [PreserveSig] int EndAudioPreroll();
    [PreserveSig] int ScheduleAudioSamples(IntPtr buffer, uint sampleFrameCount, long streamTime, long timeScale, out uint sampleFramesWritten);
    [PreserveSig] int GetBufferedAudioSampleFrameCount(out uint bufferedSampleFrameCount);
    [PreserveSig] int FlushBufferedAudioSamples();
    [PreserveSig] int SetAudioCallback(IntPtr theCallback);

    // Scheduled playback control
    [PreserveSig] int StartScheduledPlayback(long playbackStartTime, long timeScale, double playbackSpeed);
    [PreserveSig] int StopScheduledPlayback(long stopPlaybackAtTime, out long actualStopTime, long timeScale);
    [PreserveSig] int IsScheduledPlaybackRunning(out int active);
    [PreserveSig] int GetScheduledStreamTime(long timeScale, out long streamTime, out double playbackSpeed);
    [PreserveSig] int GetReferenceStatus(out uint status);
    [PreserveSig] int GetHardwareReferenceClock(long timeScale, out long hardwareTime, out long timeInFrame, out long ticksPerFrame);
}

internal sealed class DeckLinkOutputCallbackHandler : IDeckLinkVideoOutputCallback
{
    private readonly Action<long> _onCompleted;
    private long _completedCount;

    public DeckLinkOutputCallbackHandler(Action<long> onCompleted)
    {
        _onCompleted = onCompleted;
    }

    public int ScheduledFrameCompleted(IDeckLinkVideoFrame completedFrame, uint result)
    {
        Interlocked.Increment(ref _completedCount);
        _onCompleted(_completedCount);
        return 0; // S_OK
    }

    public int ScheduledPlaybackHasStopped()
    {
        return 0;
    }
}

public static class DeckLinkPlayoutEngine
{
    private static readonly Guid ClsidDeckLinkIterator = new("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676");

    public const uint BmdFormat8BitYUV = 0x32767579;  // '2vuy' = UYVY 8-bit 4:2:2
    public const uint BmdFormat10BitYUV = 0x76323130; // 'v210' = 10-bit YUV 4:2:2
    public const uint BmdFormat8BitBGRA = 0x42475241; // 'BGRA'

    public const uint BmdModeHD1080p50   = 0x48703530; // 'Hp50'
    public const uint BmdModeHD1080p6000 = 0x48703630; // 'Hp60'
    public const uint BmdModeHD1080p5994 = 0x48703539; // 'Hp59'
    public const uint BmdModeHD1080p25   = 0x48703235; // 'Hp25'
    public const uint BmdModeHD1080p2997 = 0x48703239; // 'Hp29'
    public const uint BmdModeHD1080p30   = 0x48703330; // 'Hp30'
    public const uint BmdModeHD1080i50   = 0x48693530; // 'Hi50'
    public const uint BmdModeHD1080i5994 = 0x48693539; // 'Hi59'
    public const uint BmdModeHD720p50    = 0x68703530; // 'hp50'
    public const uint BmdModeHD720p60    = 0x68703630; // 'hp60'
    public const uint BmdMode4K2160p50   = 0x346B3530; // '4k50'
    public const uint BmdMode4K2160p5994 = 0x346B3539; // '4k59'
    public const uint BmdMode4K2160p60   = 0x346B3630; // '4k60'

    public const uint BmdAudioSampleRate48kHz = 48000;
    public const uint BmdAudioSampleType16bitInteger = 16;
    public const uint BmdAudioOutputStreamContinuous = 0;

    public static uint ResolveDisplayMode(int width, int height, int fps)
    {
        if (width >= 3840 || height >= 2160)
        {
            return fps switch
            {
                60 => BmdMode4K2160p60,
                59 => BmdMode4K2160p5994,
                _ => BmdMode4K2160p50
            };
        }

        if (width <= 1280 && height <= 720)
        {
            return fps >= 59 ? BmdModeHD720p60 : BmdModeHD720p50;
        }

        // 1080 standard
        return fps switch
        {
            60 => BmdModeHD1080p6000,
            59 => BmdModeHD1080p5994,
            50 => BmdModeHD1080p50,
            30 => BmdModeHD1080p30,
            29 => BmdModeHD1080p2997,
            25 => BmdModeHD1080p25,
            _ => BmdModeHD1080p50
        };
    }

    public static uint ResolvePixelFormat(string formatName)
    {
        if (formatName.Equals("v210", StringComparison.OrdinalIgnoreCase) ||
            formatName.Contains("10bit", StringComparison.OrdinalIgnoreCase))
        {
            return BmdFormat10BitYUV;
        }
        if (formatName.Equals("BGRA", StringComparison.OrdinalIgnoreCase))
        {
            return BmdFormat8BitBGRA;
        }
        return BmdFormat8BitYUV; // default UYVY 8-bit
    }

    public static byte[] GenerateSmpte75ColorBarsUyvy(int width, int height)
    {
        // 75% SMPTE color bars in UYVY format (2 bytes per pixel)
        // Order in 32-bit word: U0, Y0, V0, Y1
        int rowBytes = width * 2;
        byte[] frame = new byte[rowBytes * height];

        // Standard 75% 8-bit Rec.709 colors in UYVY (U, Y0, V, Y1)
        (byte U, byte Y, byte V)[] eiaBars = [
            (128, 180, 128), // 75% White
            (44,  162, 142), // Yellow
            (156, 131, 44),  // Cyan
            (72,  112, 58),  // Green
            (184, 84,  198), // Magenta
            (100, 65,  212), // Red
            (212, 35,  114)  // Blue
        ];

        int barWidth = width / eiaBars.Length;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowBytes;
            for (int x = 0; x < width; x += 2)
            {
                int barIndex = Math.Clamp(x / barWidth, 0, eiaBars.Length - 1);
                var bar = eiaBars[barIndex];

                int p = rowOffset + (x * 2);
                frame[p + 0] = bar.U;
                frame[p + 1] = bar.Y;
                frame[p + 2] = bar.V;
                frame[p + 3] = bar.Y;
            }
        }

        return frame;
    }

    public static async Task PlayDiagnosticTestPatternAsync(
        int targetPort,
        int width,
        int height,
        int fps,
        CancellationToken ct,
        Action<string>? logAction = null)
    {
        logAction?.Invoke($"[TEST A] Initiating Direct Raw Test Pattern to DeckLink Port {targetPort} ({width}x{height} @ {fps}p UYVY)...");
        var testBars = GenerateSmpte75ColorBarsUyvy(width, height);
        using var stream = new LoopingBufferStream(testBars);

        await PlayRawFramesAsync(
            targetPort, width, height, fps,
            stream,
            null, // no separate audio stream
            ct,
            logAction
        );
    }

    private sealed class LoopingBufferStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public LoopingBufferStream(byte[] data)
        {
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => long.MaxValue;
        public override long Position { get => _position; set => _position = (int)value; }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_data.Length == 0 || count == 0) return 0;
            int bytesRead = 0;
            while (bytesRead < count)
            {
                int available = _data.Length - _position;
                int toCopy = Math.Min(available, count - bytesRead);
                Buffer.BlockCopy(_data, _position, buffer, offset + bytesRead, toCopy);
                bytesRead += toCopy;
                _position = (_position + toCopy) % _data.Length;
            }
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);

            int read = Read(buffer.Span);
            return ValueTask.FromResult(read);
        }

        public override int Read(Span<byte> buffer)
        {
            if (_data.Length == 0 || buffer.Length == 0) return 0;
            int bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                int available = _data.Length - _position;
                int toCopy = Math.Min(available, buffer.Length - bytesRead);
                _data.AsSpan(_position, toCopy).CopyTo(buffer.Slice(bytesRead, toCopy));
                bytesRead += toCopy;
                _position = (_position + toCopy) % _data.Length;
            }
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static async Task PlayRawFramesAsync(
        int targetPort,
        int width,
        int height,
        int fps,
        Stream rawVideoInputStream,
        Stream? rawAudioInputStream,
        CancellationToken ct,
        Action<string>? logAction = null,
        string pixelFormatName = "UYVY",
        Action<byte[], int, int, string>? onFrameDecoded = null,
        Action<byte[]>? onAudioSamples = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            logAction?.Invoke($"[SDI PLAYOUT LINUX] Operating in Linux raw stream mode on Port {targetPort} ({width}x{height} @ {fps}p {pixelFormatName})...");
            int frameBytes = pixelFormatName.Equals("v210", StringComparison.OrdinalIgnoreCase)
                ? ((width + 47) / 48) * 128 * height
                : width * height * 2; // UYVY 4:2:2

            byte[] frameBuf = new byte[frameBytes];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int totalRead = 0;
                    while (totalRead < frameBytes)
                    {
                        int r = await rawVideoInputStream.ReadAsync(frameBuf.AsMemory(totalRead, frameBytes - totalRead), ct);
                        if (r <= 0) break;
                        totalRead += r;
                    }

                    if (totalRead < frameBytes) break;

                    onFrameDecoded?.Invoke(frameBuf, width, height, pixelFormatName);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logAction?.Invoke($"[SDI PLAYOUT LINUX] Playout loop ended: {ex.Message}");
            }
            return;
        }

        var t = Type.GetTypeFromCLSID(ClsidDeckLinkIterator);
        if (t == null)
        {
            logAction?.Invoke("[SDI PLAYOUT] Blackmagic Desktop Video driver not registered.");
            return;
        }

        object? iterObj = null;
        IntPtr targetDeckLinkPtr = IntPtr.Zero;
        IDeckLinkOutput? output = null;
        IDeckLinkMutableVideoFrame? videoFrame = null;

        try
        {
            iterObj = Activator.CreateInstance(t);
            if (iterObj is not IDeckLinkIterator iterator)
            {
                logAction?.Invoke("[SDI PLAYOUT] Failed to instantiate DeckLink iterator.");
                return;
            }

            int index = 0;
            string targetDeviceName = $"Port {targetPort}";

            while (iterator.Next(out var pDeckLink) == 0 && pDeckLink != IntPtr.Zero)
            {
                index++;
                var dl = Marshal.GetObjectForIUnknown(pDeckLink) as IDeckLink;
                string dispName = "";
                dl?.GetDisplayName(out dispName);

                bool matches = (index == targetPort) ||
                               (!string.IsNullOrWhiteSpace(dispName) && dispName.Contains($"({targetPort})"));

                if (matches)
                {
                    targetDeckLinkPtr = pDeckLink;
                    targetDeviceName = !string.IsNullOrWhiteSpace(dispName) ? dispName : targetDeviceName;
                    if (dl != null) Marshal.ReleaseComObject(dl);
                    break;
                }

                if (dl != null) Marshal.ReleaseComObject(dl);
                Marshal.Release(pDeckLink);
            }

            if (targetDeckLinkPtr == IntPtr.Zero)
            {
                logAction?.Invoke($"[SDI PLAYOUT] Target DeckLink Port {targetPort} not found on this machine.");
                return;
            }

            output = Marshal.GetObjectForIUnknown(targetDeckLinkPtr) as IDeckLinkOutput;
            if (output == null)
            {
                logAction?.Invoke($"[SDI PLAYOUT] Device '{targetDeviceName}' does not support video output.");
                return;
            }

            uint displayMode = ResolveDisplayMode(width, height, fps);
            uint bmdPixelFormat = ResolvePixelFormat(pixelFormatName);

            int hr = output.EnableVideoOutput(displayMode, 0);
            if (hr != 0)
            {
                logAction?.Invoke($"[SDI PLAYOUT] EnableVideoOutput failed on '{targetDeviceName}' (hr=0x{hr:X8}).");
                return;
            }

            // Calculate exact row bytes and frame size for pixel format
            int rowBytes = bmdPixelFormat == BmdFormat10BitYUV ? ((width + 47) / 48) * 128 : width * 2;
            int frameSize = rowBytes * height;

            hr = output.CreateVideoFrame(width, height, rowBytes, bmdPixelFormat, 0, out videoFrame);
            if (hr != 0 || videoFrame == null)
            {
                logAction?.Invoke($"[SDI PLAYOUT] CreateVideoFrame failed (hr=0x{hr:X8}).");
                return;
            }

            if (videoFrame is not IDeckLinkVideoBuffer buf)
            {
                logAction?.Invoke("[SDI PLAYOUT] Video frame does not expose IDeckLinkVideoBuffer.");
                return;
            }

            // Enable synchronized audio output if audio stream is provided
            bool audioEnabled = rawAudioInputStream != null;
            if (audioEnabled)
            {
                hr = output.EnableAudioOutput(BmdAudioSampleRate48kHz, BmdAudioSampleType16bitInteger, 2, BmdAudioOutputStreamContinuous);
                if (hr == 0)
                {
                    logAction?.Invoke($"[SDI PLAYOUT] Audio Output enabled: 48kHz 16-bit 2-ch SDI/HDMI synchronized.");
                }
            }

            logAction?.Invoke($"[SDI PLAYOUT] Active: Broadcasting SDI out '{targetDeviceName}' ({width}x{height} @ {fps} fps {pixelFormatName}).");

            byte[] videoBuffer = new byte[frameSize];
            byte[] audioBuffer = new byte[3840]; // ~20ms of 48kHz stereo 16-bit (48000 * 2ch * 2bytes / 50 = 3840 bytes)
            long frameCount = 0;

            while (!ct.IsCancellationRequested)
            {
                // Read exact uncompressed video frame
                int totalRead = 0;
                while (totalRead < frameSize && !ct.IsCancellationRequested)
                {
                    int read = await rawVideoInputStream.ReadAsync(videoBuffer.AsMemory(totalRead, frameSize - totalRead), ct).ConfigureAwait(false);
                    if (read == 0) break;
                    totalRead += read;
                }

                if (totalRead < frameSize) break;

                // Push uncompressed frame to realtime in-memory distributor for preview
                onFrameDecoded?.Invoke(videoBuffer, width, height, pixelFormatName);

                // Copy uncompressed video frame into DeckLink DMA buffer
                int accessHr = buf.StartAccess(3 /* bmdBufferAccessReadWrite */);
                if (accessHr == 0)
                {
                    if (buf.GetBytes(out var pBuffer) == 0 && pBuffer != IntPtr.Zero)
                    {
                        Marshal.Copy(videoBuffer, 0, pBuffer, frameSize);
                    }
                    buf.EndAccess();
                }

                // If audio stream is available, write audio samples synchronously
                if (audioEnabled && rawAudioInputStream != null)
                {
                    int audioRead = await rawAudioInputStream.ReadAsync(audioBuffer.AsMemory(0, audioBuffer.Length), ct).ConfigureAwait(false);
                    if (audioRead > 0)
                    {
                        onAudioSamples?.Invoke(audioBuffer.AsSpan(0, audioRead).ToArray());
                        uint sampleFrames = (uint)(audioRead / 4); // 4 bytes per stereo pair
                        GCHandle handle = GCHandle.Alloc(audioBuffer, GCHandleType.Pinned);
                        try
                        {
                            output.WriteAudioSamplesSync(handle.AddrOfPinnedObject(), sampleFrames, out _);
                        }
                        catch { }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }

                // Synchronously present frame to SDI port without holding CPU buffer locks
                output.DisplayVideoFrameSync(videoFrame);
                frameCount++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logAction?.Invoke($"[SDI PLAYOUT ERROR] {ex.Message}");
        }
        finally
        {
            try
            {
                output?.DisableAudioOutput();
                output?.DisableVideoOutput();
            }
            catch { }

            if (videoFrame != null)
            {
                try { Marshal.ReleaseComObject(videoFrame); } catch { }
            }
            if (output != null)
            {
                try { Marshal.ReleaseComObject(output); } catch { }
            }
            if (targetDeckLinkPtr != IntPtr.Zero)
            {
                try { Marshal.Release(targetDeckLinkPtr); } catch { }
            }
            if (iterObj != null)
            {
                try { Marshal.ReleaseComObject(iterObj); } catch { }
            }
            logAction?.Invoke($"[SDI PLAYOUT] Stopped SDI output on Port {targetPort}.");
        }
    }
}
