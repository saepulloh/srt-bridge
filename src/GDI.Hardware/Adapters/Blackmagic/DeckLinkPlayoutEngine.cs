using System.Runtime.InteropServices;

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
}

public static class DeckLinkPlayoutEngine
{
    private static readonly Guid ClsidDeckLinkIterator = new("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676");

    public const uint BmdFormat8BitYUV = 0x32767579; // '2vuy' = UYVY
    public const uint BmdModeHD1080p50 = 0x48703530; // 'Hp50'
    public const uint BmdModeHD1080p6000 = 0x48703630; // 'Hp60'
    public const uint BmdModeHD1080p5994 = 0x48703539; // 'Hp59'
    public const uint BmdModeHD1080p25 = 0x48703235; // 'Hp25'
    public const uint BmdModeHD1080p2997 = 0x48703239; // 'Hp29'
    public const uint BmdModeHD1080p30 = 0x48703330; // 'Hp30'
    public const uint BmdModeHD1080i50 = 0x48693530; // 'Hi50'
    public const uint BmdModeHD1080i5994 = 0x48693539; // 'Hi59'
    public const uint BmdModeHD720p50 = 0x68703530; // 'hp50'
    public const uint BmdModeHD720p60 = 0x68703630; // 'hp60'

    public static uint ResolveDisplayMode(int width, int height, int fps)
    {
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
            25 => BmdModeHD1080p25,
            _ => BmdModeHD1080p50 // Default standard broadcast format
        };
    }

    public static async Task PlayRawFramesAsync(
        int targetPort,
        int width,
        int height,
        int fps,
        Stream rawInputStream,
        CancellationToken ct,
        Action<string>? logAction = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            logAction?.Invoke("[SDI PLAYOUT] Native DeckLink is only supported on Windows.");
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
        IDeckLinkMutableVideoFrame? frame = null;

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
            int hr = output.EnableVideoOutput(displayMode, 0);
            if (hr != 0)
            {
                logAction?.Invoke($"[SDI PLAYOUT] EnableVideoOutput failed on '{targetDeviceName}' (hr=0x{hr:X8}).");
                return;
            }

            int rowBytes = width * 2; // UYVY 16bpp
            int frameSize = rowBytes * height;

            hr = output.CreateVideoFrame(width, height, rowBytes, BmdFormat8BitYUV, 0, out frame);
            if (hr != 0 || frame == null)
            {
                logAction?.Invoke($"[SDI PLAYOUT] CreateVideoFrame failed (hr=0x{hr:X8}).");
                return;
            }

            if (frame is not IDeckLinkVideoBuffer buf)
            {
                logAction?.Invoke("[SDI PLAYOUT] Video frame does not expose IDeckLinkVideoBuffer.");
                return;
            }

            hr = buf.StartAccess(3 /* bmdBufferAccessReadWrite */);
            if (hr != 0)
            {
                logAction?.Invoke($"[SDI PLAYOUT] StartAccess failed (hr=0x{hr:X8}).");
                return;
            }

            hr = buf.GetBytes(out var pBuffer);
            if (hr != 0 || pBuffer == IntPtr.Zero)
            {
                buf.EndAccess();
                logAction?.Invoke($"[SDI PLAYOUT] GetBytes failed (hr=0x{hr:X8}).");
                return;
            }

            logAction?.Invoke($"[SDI PLAYOUT] Active: Transmitting SDI out '{targetDeviceName}' ({width}x{height} @ {fps} fps UYVY).");

            byte[] readBuffer = new byte[frameSize];

            while (!ct.IsCancellationRequested)
            {
                int totalRead = 0;
                while (totalRead < frameSize && !ct.IsCancellationRequested)
                {
                    int read = await rawInputStream.ReadAsync(readBuffer.AsMemory(totalRead, frameSize - totalRead), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        // End of stream
                        break;
                    }
                    totalRead += read;
                }

                if (totalRead < frameSize)
                {
                    break;
                }

                // Copy raw frame directly into DeckLink frame buffer
                Marshal.Copy(readBuffer, 0, pBuffer, frameSize);

                // Synchronously display frame through SDI port
                output.DisplayVideoFrameSync(frame);
            }

            buf.EndAccess();
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
                output?.DisableVideoOutput();
            }
            catch { }

            if (frame != null)
            {
                try { Marshal.ReleaseComObject(frame); } catch { }
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
