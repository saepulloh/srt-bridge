using GDI.Core.Models;
using GDI.Encoder;
using GDI.SRT.Engine;
using Xunit;

namespace GDI.Tests;

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("50FB36CD-3063-4B73-BDBB-958087F2D8BA"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkIteratorTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int Next(out IntPtr deckLinkInstance);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("C418FBDD-0587-48ED-8FE5-640F0A14AF91"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int GetModelName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.BStr)] out string modelName);
    [System.Runtime.InteropServices.PreserveSig]
    int GetDisplayName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.BStr)] out string displayName);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("6502091C-615F-4F51-BAF6-45C4256DD5B0"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkVideoFrameTest
{
    [System.Runtime.InteropServices.PreserveSig] int GetWidth();
    [System.Runtime.InteropServices.PreserveSig] int GetHeight();
    [System.Runtime.InteropServices.PreserveSig] int GetRowBytes();
    [System.Runtime.InteropServices.PreserveSig] uint GetPixelFormat();
    [System.Runtime.InteropServices.PreserveSig] uint GetFlags();
    [System.Runtime.InteropServices.PreserveSig] int GetTimecode(uint format, out IntPtr timecode);
    [System.Runtime.InteropServices.PreserveSig] int GetAncillaryData(out IntPtr ancillary);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("CF9EB134-0374-4C5B-95FA-1EC14819FF62"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkMutableVideoFrameTest : IDeckLinkVideoFrameTest
{
    [System.Runtime.InteropServices.PreserveSig] new int GetWidth();
    [System.Runtime.InteropServices.PreserveSig] new int GetHeight();
    [System.Runtime.InteropServices.PreserveSig] new int GetRowBytes();
    [System.Runtime.InteropServices.PreserveSig] new uint GetPixelFormat();
    [System.Runtime.InteropServices.PreserveSig] new uint GetFlags();
    [System.Runtime.InteropServices.PreserveSig] new int GetTimecode(uint format, out IntPtr timecode);
    [System.Runtime.InteropServices.PreserveSig] new int GetAncillaryData(out IntPtr ancillary);

    [System.Runtime.InteropServices.PreserveSig] int SetFlags(uint flags);
    [System.Runtime.InteropServices.PreserveSig] int SetTimecode(uint format, IntPtr timecode);
    [System.Runtime.InteropServices.PreserveSig] int SetTimecodeFromComponents(uint format, byte hours, byte minutes, byte seconds, byte frames, uint flags);
    [System.Runtime.InteropServices.PreserveSig] int SetAncillaryData(IntPtr ancillary);
    [System.Runtime.InteropServices.PreserveSig] int SetTimecodeUserBits(uint format, uint userBits);
    [System.Runtime.InteropServices.PreserveSig] int SetInterfaceProvider(IntPtr iface, IntPtr provider);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("1A8077F1-9FE2-4533-8147-2294305E253F"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkOutputTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int DoesSupportVideoMode(long connection, uint displayMode, uint pixelFormat, uint conversionMode, uint flags, out uint resultDisplayMode, out int supported);
    [System.Runtime.InteropServices.PreserveSig]
    int GetDisplayMode(uint displayMode, out IntPtr resultDisplayMode);
    [System.Runtime.InteropServices.PreserveSig]
    int GetDisplayModeIterator(out IntPtr iterator);
    [System.Runtime.InteropServices.PreserveSig]
    int SetScreenPreviewCallback(IntPtr previewCallback);
    [System.Runtime.InteropServices.PreserveSig]
    int EnableVideoOutput(uint displayMode, uint outputFlags);
    [System.Runtime.InteropServices.PreserveSig]
    int DisableVideoOutput();
    [System.Runtime.InteropServices.PreserveSig]
    int CreateVideoFrame(int width, int height, int rowBytes, uint pixelFormat, uint flags, out IDeckLinkMutableVideoFrameTest outFrame);
    [System.Runtime.InteropServices.PreserveSig]
    int CreateVideoFrameWithBuffer(int width, int height, int rowBytes, uint pixelFormat, uint flags, IntPtr buffer, out IDeckLinkMutableVideoFrameTest outFrame);
    [System.Runtime.InteropServices.PreserveSig]
    int RowBytesForPixelFormat(uint pixelFormat, int width, out int rowBytes);
    [System.Runtime.InteropServices.PreserveSig]
    int CreateAncillaryData(uint pixelFormat, out IntPtr outBuffer);
    [System.Runtime.InteropServices.PreserveSig]
    int DisplayVideoFrameSync(IDeckLinkVideoFrameTest theFrame);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("CCB4B64A-5C86-4E02-B778-885D352709FE"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkVideoBufferTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int GetBytes(out IntPtr buffer);
    [System.Runtime.InteropServices.PreserveSig]
    int StartAccess(uint accessMode);
    [System.Runtime.InteropServices.PreserveSig]
    int EndAccess();
}

public class SrtAndEncoderTests
{
    [Fact]
    public void SrtUriBuilder_BuildsCallerUriCorrectly()
    {
        var channel = new ChannelConfig
        {
            Name = "TEST_CALLER",
            Mode = SrtMode.Caller,
            Host = "192.168.10.150",
            Port = 9002,
            LatencyMs = 200,
            Passphrase = "SecretPassphrase123",
            PbKeyLength = 32,
            StreamId = "live/feed1"
        };

        var uri = SrtUriBuilder.BuildFromChannel(channel, redactPassphrase: false);
        Assert.StartsWith("srt://192.168.10.150:9002?", uri);
        Assert.Contains("mode=caller", uri);
        Assert.Contains("latency=200", uri);
        Assert.Contains("passphrase=SecretPassphrase123", uri);
        Assert.Contains("pbkeylen=32", uri);
        Assert.Contains("streamid=live%2Ffeed1", uri);

        var redacted = SrtUriBuilder.BuildFromChannel(channel, redactPassphrase: true);
        Assert.Contains("passphrase=[REDACTED]", redacted);
        Assert.DoesNotContain("SecretPassphrase123", redacted);
    }

    [Fact]
    public void SrtUriBuilder_BuildsListenerUriCorrectly()
    {
        var channel = new ChannelConfig
        {
            Name = "TEST_LISTENER",
            Mode = SrtMode.Listener,
            Port = 9005,
            LatencyMs = 150
        };

        var uri = SrtUriBuilder.BuildFromChannel(channel);
        Assert.StartsWith("srt://0.0.0.0:9005?", uri);
        Assert.Contains("mode=listener", uri);
        Assert.Contains("latency=150", uri);
    }

    [Fact]
    public void SrtUriBuilder_EncodesStreamIdForEncoderAndDecoder()
    {
        var encoder = new ChannelConfig
        {
            Name = "TEST_ENC_STREAMID",
            Direction = ChannelDirection.Encoder,
            Mode = SrtMode.Caller,
            Host = "10.0.0.50",
            Port = 9000,
            StreamId = "live/primary?auth=token123"
        };
        var encUri = SrtUriBuilder.BuildFromChannel(encoder);
        Assert.Contains("streamid=live%2Fprimary%3Fauth%3Dtoken123", encUri);

        var decoder = new ChannelConfig
        {
            Name = "TEST_DEC_STREAMID",
            Direction = ChannelDirection.Decoder,
            Mode = SrtMode.Listener,
            Port = 9001,
            StreamId = "#!::u=studio,r=feed01,m=publish"
        };
        var decUri = SrtUriBuilder.BuildFromChannel(decoder);
        Assert.Contains("streamid=%23%21%3A%3Au%3Dstudio%2Cr%3Dfeed01%2Cm%3Dpublish", decUri);
    }

    [Fact]
    public void SrtMetricsParser_ParsesFfmpegProgressLine()
    {
        const string sampleLine = "frame=  240 fps=60.0 q=24.0 size=    2048kB time=00:00:04.00 bitrate=4194.3kbits/s speed=1.00x drop=2";
        var metrics = new ChannelMetrics();

        SrtMetricsParser.ParseProgressLine(sampleLine, metrics);

        Assert.Equal(60.0, metrics.CurrentFps);
        Assert.Equal(4194.3, metrics.CurrentBitrateKbps);
        Assert.Equal(240, metrics.TotalFrames);
        Assert.Equal(2, metrics.DroppedFrames);
    }

    [Fact]
    public void EncoderArguments_ContainsRequiredLowLatencyFlags()
    {
        var channel = new ChannelConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            VideoBitrateKbps = 8000,
            InputBackend = "dshow",
            VideoDevice = "DeckLink SDI",
            IncludeAudio = true,
            AudioDevice = "DeckLink Audio",
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc").ToList();

        Assert.Contains("-f", args);
        Assert.Contains("dshow", args);
        Assert.Contains("video=DeckLink SDI:audio=DeckLink Audio", args);
        Assert.Contains("-c:v", args);
        Assert.Contains("h264_nvenc", args);
        Assert.Contains("-b:v", args);
        Assert.Contains("8000k", args);
        Assert.Contains("-tune", args);
        Assert.Contains("ll", args);
        Assert.Contains("-bf", args);
        Assert.Contains("0", args);
        Assert.Contains("-spatial-aq", args);
        Assert.Contains("1", args);
        Assert.Contains("-profile:v", args);
        Assert.Contains("high", args);
    }

    [Fact]
    public void EncoderArguments_AutoResolution_PassThroughWithoutForcedScale()
    {
        var channel = new ChannelConfig
        {
            Width = 0,
            Height = 0,
            Fps = 0,
            VideoBitrateKbps = 6000,
            InputBackend = "dshow",
            VideoDevice = "vMix Video",
            IncludeAudio = false,
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc").ToList();

        Assert.Contains("-f", args);
        Assert.Contains("dshow", args);
        Assert.Contains("video=vMix Video", args);
        
        var mpegtsIdx = args.IndexOf("mpegts");
        var primaryArgs = args.Take(mpegtsIdx).ToList();
        Assert.DoesNotContain("-video_size", primaryArgs);
        Assert.DoesNotContain("-vf", primaryArgs);

        Assert.Contains("-c:v", args);
        Assert.Contains("h264_nvenc", args);
    }

    [Fact]
    public void EncoderArguments_DeckLink_EnforcesSdiFormatAndFramerate()
    {
        var channel = new ChannelConfig
        {
            Width = 0,
            Height = 0,
            Fps = 0,
            VideoBitrateKbps = 8000,
            InputBackend = "dshow",
            VideoDevice = "Decklink Video Capture (2)",
            IncludeAudio = true,
            AudioDevice = "Decklink Audio Capture (2)",
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc").ToList();

        Assert.Contains("-video_size", args);
        Assert.Contains("1920x1080", args);
        Assert.Contains("-framerate", args);
        Assert.Contains("50", args);
        Assert.Contains("-pixel_format", args);
        Assert.Contains("uyvy422", args);
        Assert.Contains("video=Decklink Video Capture (2):audio=Decklink Audio Capture (2)", args);
    }

    [Fact]
    public void EncoderArguments_DeckLink_WithDetectedSignal_AppliesDetectedParameters()
    {
        var channel = new ChannelConfig
        {
            Width = 0,
            Height = 0,
            Fps = 0,
            VideoBitrateKbps = 8000,
            InputBackend = "dshow",
            VideoDevice = "Decklink Video Capture (3)",
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc", 1920, 1080, 25).ToList();

        Assert.Contains("-video_size", args);
        Assert.Contains("1920x1080", args);
        Assert.Contains("-framerate", args);
        Assert.Contains("25", args);
    }

    [Fact]
    public void EncoderArguments_Output2Preview_ContainsExplicitMjpegAndNoAudio()
    {
        var channel = new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Width = 1920,
            Height = 1080,
            Fps = 25,
            InputBackend = "dshow",
            VideoDevice = "Decklink Video Capture",
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc").ToList();

        Assert.Contains("-c:v", args);
        Assert.Contains("mjpeg", args);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void DirectShow_RenderFile_To_DecklinkVideoRender()
    {
        if (!OperatingSystem.IsWindows()) return;
        var graph = new DirectShowLib.FilterGraph() as DirectShowLib.IFilterGraph2;
        Assert.NotNull(graph);

        var clsid = new Guid("4354ED19-BDE1-4083-9BE7-991AF1F0B527"); // Decklink Video Render (3)
        var t = Type.GetTypeFromCLSID(clsid);
        Assert.NotNull(t);
        var decklinkFilter = Activator.CreateInstance(t) as DirectShowLib.IBaseFilter;
        Assert.NotNull(decklinkFilter);

        int hr = graph.AddFilter(decklinkFilter, "Decklink Video Render (3)");
        Assert.Equal(0, hr);

        var testFile = @"C:\Users\GEDE NEW\Music\GDI\test_dl1.mp4";
        var logSb = new System.Text.StringBuilder();
        if (File.Exists(testFile))
        {
            hr = graph.RenderFile(testFile, null);
            logSb.AppendLine($"graph.RenderFile hr=0x{hr:X8}");

            hr = graph.EnumFilters(out var enumFilters);
            if (hr == 0 && enumFilters != null)
            {
                var f = new DirectShowLib.IBaseFilter[1];
                while (enumFilters.Next(1, f, IntPtr.Zero) == 0 && f[0] != null)
                {
                    f[0].QueryFilterInfo(out var fi);
                    logSb.AppendLine($"Graph Filter: {fi.achName}");
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(f[0]);
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(enumFilters);
            }
            File.WriteAllText("graph_filters.txt", logSb.ToString());
        }

        System.Runtime.InteropServices.Marshal.ReleaseComObject(decklinkFilter);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(graph);
    }

    [Fact]
    public void DirectShow_InspectDecklinkPinsAndTypes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sb = new System.Text.StringBuilder();
        var clsid = new Guid("4354ED19-BDE1-4083-9BE7-991AF1F0B527"); // Decklink Video Render (3)
        var t = Type.GetTypeFromCLSID(clsid);
        Assert.NotNull(t);
        var decklinkFilter = Activator.CreateInstance(t) as DirectShowLib.IBaseFilter;
        Assert.NotNull(decklinkFilter);

        int hr = decklinkFilter.EnumPins(out var enumPins);
        Assert.Equal(0, hr);
        var pins = new DirectShowLib.IPin[1];
        while (enumPins.Next(1, pins, IntPtr.Zero) == 0 && pins[0] != null)
        {
            pins[0].QueryPinInfo(out var pinInfo);
            sb.AppendLine($"PIN: '{pinInfo.name}' Direction: {pinInfo.dir}");

            hr = pins[0].EnumMediaTypes(out var enumTypes);
            if (hr == 0 && enumTypes != null)
            {
                var mts = new DirectShowLib.AMMediaType[1];
                int count = 0;
                while (enumTypes.Next(1, mts, IntPtr.Zero) == 0 && mts[0] != null && count < 10)
                {
                    count++;
                    sb.AppendLine($"  MT[{count}]: Major={mts[0].majorType} Sub={mts[0].subType} FormatType={mts[0].formatType}");
                    DirectShowLib.DsUtils.FreeAMMediaType(mts[0]);
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(enumTypes);
            }
            System.Runtime.InteropServices.Marshal.ReleaseComObject(pins[0]);
        }
        System.Runtime.InteropServices.Marshal.ReleaseComObject(enumPins);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(decklinkFilter);
        File.WriteAllText("decklink_pin_info.txt", sb.ToString());
    }

    [Fact]
    public void DirectShow_TestDecklinkAcceptedMediaTypes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var clsid = new Guid("4354ED19-BDE1-4083-9BE7-991AF1F0B527"); // Decklink Video Render (3)
        var t = Type.GetTypeFromCLSID(clsid);
        Assert.NotNull(t);
        var decklinkFilter = Activator.CreateInstance(t) as DirectShowLib.IBaseFilter;
        Assert.NotNull(decklinkFilter);

        int hr = decklinkFilter.FindPin("In", out var inPin);
        Assert.Equal(0, hr);
        Assert.NotNull(inPin);

        var subtypes = new Dictionary<string, Guid>
        {
            { "UYVY", DirectShowLib.MediaSubType.UYVY },
            { "HDYC", new Guid("43594448-0000-0010-8000-00AA00389B71") },
            { "YUY2", DirectShowLib.MediaSubType.YUY2 },
            { "RGB24", DirectShowLib.MediaSubType.RGB24 },
            { "RGB32", DirectShowLib.MediaSubType.RGB32 },
            { "ARGB32", DirectShowLib.MediaSubType.ARGB32 },
            { "NV12", DirectShowLib.MediaSubType.NV12 }
        };

        var sb = new System.Text.StringBuilder();

        foreach (var kvp in subtypes)
        {
            var vih = new DirectShowLib.VideoInfoHeader
            {
                BmiHeader = new DirectShowLib.BitmapInfoHeader
                {
                    Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DirectShowLib.BitmapInfoHeader)),
                    Width = 1920,
                    Height = 1080,
                    Planes = 1,
                    BitCount = kvp.Key == "RGB24" ? (short)24 : (short)16,
                    Compression = (kvp.Key == "RGB24" || kvp.Key == "RGB32") ? 0 : 0x59565955 // UYVY fourcc
                },
                AvgTimePerFrame = 400000 // 25 fps
            };

            var mt = new DirectShowLib.AMMediaType
            {
                majorType = DirectShowLib.MediaType.Video,
                subType = kvp.Value,
                formatType = DirectShowLib.FormatType.VideoInfo,
                formatPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(vih))
            };
            System.Runtime.InteropServices.Marshal.StructureToPtr(vih, mt.formatPtr, false);
            mt.formatSize = System.Runtime.InteropServices.Marshal.SizeOf(vih);

            hr = inPin.QueryAccept(mt);
            sb.AppendLine($"Subtype {kvp.Key}: hr=0x{hr:X8} ({(hr == 0 ? "ACCEPTED" : "REJECTED")})");

            System.Runtime.InteropServices.Marshal.FreeHGlobal(mt.formatPtr);
        }

        System.Runtime.InteropServices.Marshal.ReleaseComObject(inPin);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(decklinkFilter);
        File.WriteAllText("decklink_accepted_types.txt", sb.ToString());
    }

    [Fact]
    public void DeckLink_TestNativeComDiscovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sb = new System.Text.StringBuilder();

        var clsids = new Dictionary<string, Guid>
        {
            { "CDeckLinkIterator_10_8", new Guid("1073A05C-D885-47E9-B3C6-129B3F9F648B") },
            { "CDeckLinkDiscovery", new Guid("22FBFC33-8D07-495C-A5BF-DAB5EA9B82DB") },
            { "CDeckLinkAPI", new Guid("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676") }
        };

        foreach (var kvp in clsids)
        {
            try
            {
                var t = Type.GetTypeFromCLSID(kvp.Value);
                if (t != null)
                {
                    var inst = Activator.CreateInstance(t);
                    sb.AppendLine($"CLSID {kvp.Key}: SUCCESS inst={inst != null} type={inst?.GetType().FullName}");
                    if (inst != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(inst);
                }
                else
                {
                    sb.AppendLine($"CLSID {kvp.Key}: Type is null");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"CLSID {kvp.Key}: EXCEPTION {ex.Message}");
            }
        }
        File.WriteAllText("decklink_native_com.txt", sb.ToString());
    }

    [Fact]
    public void DeckLink_EnumerateDevicesViaIterator()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sb = new System.Text.StringBuilder();

        var iterGuid = new Guid("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676"); // CDeckLinkIterator
        var t = Type.GetTypeFromCLSID(iterGuid);
        Assert.NotNull(t);
        var iterObj = Activator.CreateInstance(t);
        Assert.NotNull(iterObj);

        var iterator = iterObj as IDeckLinkIteratorTest;
        sb.AppendLine($"Cast to IDeckLinkIteratorTest: {(iterator != null ? "SUCCESS" : "FAILED")}");

        if (iterator != null)
        {
            int index = 0;
            while (iterator.Next(out var pDeckLink) == 0 && pDeckLink != IntPtr.Zero)
            {
                index++;
                var dl = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(pDeckLink) as IDeckLinkTest;
                string modelName = "Unknown";
                string dispName = "Unknown";
                if (dl != null)
                {
                    dl.GetModelName(out modelName);
                    dl.GetDisplayName(out dispName);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(dl);
                }
                sb.AppendLine($"Device {index}: Model='{modelName}' Display='{dispName}' ptr=0x{pDeckLink.ToInt64():X}");
                System.Runtime.InteropServices.Marshal.Release(pDeckLink);
            }
        }

        System.Runtime.InteropServices.Marshal.ReleaseComObject(iterObj);
        File.WriteAllText("decklink_devices_enum.txt", sb.ToString());
    }

    [System.Runtime.InteropServices.DllImport("oleaut32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string szFile, int regkind, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] out object pptlib);

    [Fact]
    public void DeckLink_DumpTypeLibInterfaces()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dllPath = @"C:\Program Files\Blackmagic Design\Blackmagic Desktop Video\DeckLinkAPI64.dll";
        if (!File.Exists(dllPath)) return;

        LoadTypeLibEx(dllPath, 2 /* REGKIND_NONE */, out var tlibObj);
        Assert.NotNull(tlibObj);

        var tlib = tlibObj as System.Runtime.InteropServices.ComTypes.ITypeLib;
        Assert.NotNull(tlib);

        int count = tlib.GetTypeInfoCount();
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < count; i++)
        {
            tlib.GetTypeInfo(i, out var tinfo);
            tinfo.GetDocumentation(-1, out var strName, out _, out _, out _);
            tinfo.GetTypeAttr(out var pAttr);
            var attr = System.Runtime.InteropServices.Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pAttr);
            sb.AppendLine($"{attr.typekind}: {strName} = {attr.guid}");
            tinfo.ReleaseTypeAttr(pAttr);
        }

        File.WriteAllText("decklink_typelib.txt", sb.ToString());
    }

    [Fact]
    public void DeckLink_DumpOutputMethods()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dllPath = @"C:\Program Files\Blackmagic Design\Blackmagic Desktop Video\DeckLinkAPI64.dll";
        if (!File.Exists(dllPath)) return;

        LoadTypeLibEx(dllPath, 2 /* REGKIND_NONE */, out var tlibObj);
        var tlib = tlibObj as System.Runtime.InteropServices.ComTypes.ITypeLib;
        Assert.NotNull(tlib);

        int count = tlib.GetTypeInfoCount();
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < count; i++)
        {
            tlib.GetTypeInfo(i, out var tinfo);
            tinfo.GetDocumentation(-1, out var strName, out _, out _, out _);
            if (strName == "IDeckLinkOutput" || strName == "IDeckLinkMutableVideoFrame" || strName == "IDeckLinkVideoFrame" || strName == "IDeckLinkVideoBuffer")
            {
                tinfo.GetTypeAttr(out var pAttr);
                var attr = System.Runtime.InteropServices.Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pAttr);
                sb.AppendLine($"=== INTERFACE: {strName} ({attr.guid}) ===");
                for (int m = 0; m < attr.cFuncs; m++)
                {
                    tinfo.GetFuncDesc(m, out var pFunc);
                    var func = System.Runtime.InteropServices.Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.FUNCDESC>(pFunc);
                    tinfo.GetDocumentation(func.memid, out var fnName, out _, out _, out _);
                    sb.AppendLine($"  [{m}] {fnName} (invkind={func.invkind}, callconv={func.callconv}, cParams={func.cParams})");
                    tinfo.ReleaseFuncDesc(pFunc);
                }
                tinfo.ReleaseTypeAttr(pAttr);
            }
        }

        File.WriteAllText("decklink_output_methods.txt", sb.ToString());
    }

    [Fact]
    public void DeckLink_TestOutputOnPort3()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sb = new System.Text.StringBuilder();

        var iterGuid = new Guid("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676"); // CDeckLinkIterator
        var t = Type.GetTypeFromCLSID(iterGuid);
        Assert.NotNull(t);
        var iterObj = Activator.CreateInstance(t);
        Assert.NotNull(iterObj);

        var iterator = iterObj as IDeckLinkIteratorTest;
        Assert.NotNull(iterator);

        IntPtr port3Ptr = IntPtr.Zero;
        string port3Name = "";
        int index = 0;
        while (iterator.Next(out var pDeckLink) == 0 && pDeckLink != IntPtr.Zero)
        {
            index++;
            var dl = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(pDeckLink) as IDeckLinkTest;
            if (dl != null)
            {
                dl.GetDisplayName(out var dispName);
                if (index == 3 || dispName.Contains("3"))
                {
                    port3Ptr = pDeckLink;
                    port3Name = dispName;
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(dl);
                    break;
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(dl);
            }
            System.Runtime.InteropServices.Marshal.Release(pDeckLink);
        }

        sb.AppendLine($"Port 3: name='{port3Name}' ptr=0x{port3Ptr.ToInt64():X}");
        Assert.True(port3Ptr != IntPtr.Zero, "DeckLink Port 3 must be found!");

        var outputObj = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(port3Ptr) as IDeckLinkOutputTest;
        sb.AppendLine($"Cast to IDeckLinkOutputTest: {(outputObj != null ? "SUCCESS" : "FAILED")}");
        Assert.NotNull(outputObj);

        // Enable HD 1080p50 ('Hp50' = 0x48703530)
        uint bmdModeHD1080p50 = 0x48703530;
        uint bmdFormat8BitYUV = 0x32767579; // '2vuy' = UYVY
        int hr = outputObj.EnableVideoOutput(bmdModeHD1080p50, 0);
        sb.AppendLine($"EnableVideoOutput: hr=0x{hr:X8}");

        if (hr == 0)
        {
            hr = outputObj.CreateVideoFrame(1920, 1080, 1920 * 2, bmdFormat8BitYUV, 0, out var frame);
            sb.AppendLine($"CreateVideoFrame: hr=0x{hr:X8}");

            if (hr == 0 && frame != null)
            {
                var bufObj = frame as IDeckLinkVideoBufferTest;
                sb.AppendLine($"Cast frame to IDeckLinkVideoBufferTest: {(bufObj != null ? "SUCCESS" : "FAILED")}");
                if (bufObj != null)
                {
                    hr = bufObj.StartAccess(3 /* bmdBufferAccessReadWrite */);
                    sb.AppendLine($"StartAccess: hr=0x{hr:X8}");
                    hr = bufObj.GetBytes(out var pBuffer);
                    sb.AppendLine($"GetBytes: hr=0x{hr:X8} ptr=0x{pBuffer.ToInt64():X}");
                    if (hr == 0 && pBuffer != IntPtr.Zero)
                    {
                        // Fill with 75% SMPTE color bars (UYVY)
                        byte[] barPixels = new byte[1920 * 2];
                        int barWidth = 1920 / 7;
                        byte[][] yuvBars = new byte[][]
                        {
                            new byte[] { 128, 180, 128, 180 }, // White (75%)
                            new byte[] { 44, 162, 142, 162 },  // Yellow
                            new byte[] { 156, 131, 44, 131 },  // Cyan
                            new byte[] { 72, 112, 58, 112 },   // Green
                            new byte[] { 184, 84, 198, 84 },   // Magenta
                            new byte[] { 100, 65, 212, 65 },   // Red
                            new byte[] { 212, 35, 114, 35 },   // Blue
                        };

                        for (int col = 0; col < 1920; col += 2)
                        {
                            int barIdx = Math.Min(col / barWidth, 6);
                            var pat = yuvBars[barIdx];
                            barPixels[col * 2] = pat[0];
                            barPixels[col * 2 + 1] = pat[1];
                            barPixels[col * 2 + 2] = pat[2];
                            barPixels[col * 2 + 3] = pat[3];
                        }

                        for (int line = 0; line < 1080; line++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(barPixels, 0, pBuffer + (line * 1920 * 2), 1920 * 2);
                        }

                        bufObj.EndAccess();

                        hr = outputObj.DisplayVideoFrameSync(frame);
                        sb.AppendLine($"DisplayVideoFrameSync: hr=0x{hr:X8} (OUTPUT SENT TO PORT 3!)");
                    }
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(frame);
            }

            outputObj.DisableVideoOutput();
            sb.AppendLine("DisableVideoOutput: Clean shutdown.");
        }

        System.Runtime.InteropServices.Marshal.ReleaseComObject(outputObj);
        System.Runtime.InteropServices.Marshal.Release(port3Ptr);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(iterObj);
    }

    [Fact]
    public void DeckLinkPlayoutEngine_ResolvesCorrectDisplayModes()
    {
        Assert.Equal(GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.BmdModeHD1080p50, GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.ResolveDisplayMode(1920, 1080, 50));
        Assert.Equal(GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.BmdModeHD1080p6000, GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.ResolveDisplayMode(1920, 1080, 60));
        Assert.Equal(GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.BmdModeHD1080p25, GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.ResolveDisplayMode(1920, 1080, 25));
        Assert.Equal(GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.BmdModeHD720p50, GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.ResolveDisplayMode(1280, 720, 50));
    }

    [Fact]
    public void AudioMeterEngine_ProcessesTruePcmDspCorrectly()
    {
        var meter = new GDI.Core.Services.AudioMeterEngine();

        // 1. Test Silent Buffer (all zeros)
        byte[] silentPcm = new byte[3840]; // ~20ms stereo 48kHz
        var silentState = meter.ProcessPcm16Stereo(silentPcm);
        Assert.True(silentState.IsSilent);
        Assert.True(silentState.PeakLeftDb <= -58f);
        Assert.False(silentState.IsClippingLeft);

        // 2. Test 0 dBFS Full Scale Square Wave
        byte[] fullScalePcm = new byte[3840];
        for (int i = 0; i < fullScalePcm.Length; i += 4)
        {
            // Left channel full scale positive (32767 = 0x7FFF)
            fullScalePcm[i] = 0xFF;
            fullScalePcm[i + 1] = 0x7F;
            // Right channel full scale negative (-32768 = 0x8000)
            fullScalePcm[i + 2] = 0x00;
            fullScalePcm[i + 3] = 0x80;
        }

        var fullState = meter.ProcessPcm16Stereo(fullScalePcm);
        Assert.False(fullState.IsSilent);
        Assert.True(fullState.IsClippingLeft);
        Assert.True(fullState.IsClippingRight);
        Assert.InRange(fullState.PeakLeftDb, -0.05f, 0.05f);
        Assert.InRange(fullState.PeakRightDb, -0.05f, 0.05f);
    }

    [Fact]
    public void DeckLinkPlayoutEngine_GeneratesValidSmpte75ColorBars()
    {
        int w = 1920;
        int h = 1080;
        var bars = GDI.Hardware.Adapters.Blackmagic.DeckLinkPlayoutEngine.GenerateSmpte75ColorBarsUyvy(w, h);
        Assert.NotNull(bars);
        Assert.Equal(w * 2 * h, bars.Length); // 4,147,200 bytes for 1080p UYVY
    }

    [Fact]
    public void SrtMetricsParser_ParsesAstatsRealAudioAccurately()
    {
        var metrics = new ChannelMetrics();
        SrtMetricsParser.ParseProgressLine("lavfi.astats.1.RMS_level=-18.452000", metrics);
        SrtMetricsParser.ParseProgressLine("lavfi.astats.2.RMS_level=-18.452000", metrics);
        SrtMetricsParser.ParseProgressLine("lavfi.astats.1.Peak_level=-12.040000", metrics);
        SrtMetricsParser.ParseProgressLine("lavfi.astats.2.Peak_level=-12.040000", metrics);

        Assert.Equal(-18.452000f, metrics.RmsLeftDb, 3);
        Assert.Equal(-18.452000f, metrics.RmsRightDb, 3);
        Assert.Equal(-12.040000f, metrics.PeakLeftDb, 3);
        Assert.Equal(-12.040000f, metrics.PeakRightDb, 3);
        Assert.False(metrics.IsAudioClippingLeft);
        Assert.False(metrics.IsAudioSilent);
    }

    [Fact]
    public void EncoderEngine_BuildsQualityProfileArgumentsProperly()
    {
        var ch = new ChannelConfig
        {
            Name = "TEST_MAX_QUAL",
            VideoBitrateKbps = 25000,
            AudioBitrateKbps = 320,
            QualityProfile = "MaximumQuality",
            Width = 1920,
            Height = 1080,
            Fps = 50,
            IncludeAudio = true,
            InputBackend = "lavfi"
        };

        var args = EncoderEngine.BuildEncoderArguments(ch, "h264_nvenc", 1920, 1080, 50).ToList();
        Assert.Contains("-preset", args);
        Assert.Contains("p7", args);
        Assert.Contains("-spatial-aq", args);
        Assert.Contains("-temporal-aq", args);
        Assert.Contains("-color_primaries", args);
        Assert.Contains("bt709", args);
    }

    [Fact]
    public void Bt709Matrix_ConvertsBroadcastLimitedRangeAccurately()
    {
        // 1. Studio White: Y=235, U=128, V=128 -> Full RGB (255, 255, 255)
        int uW = 128 - 128;
        int yW = 235 - 16;
        int vW = 128 - 128;
        int rTermW = 459 * vW + 128;
        int gTermW = -55 * uW - 136 * vW + 128;
        int bTermW = 541 * uW + 128;
        int cW = 298 * yW;
        int rWhite = Math.Clamp((cW + rTermW) >> 8, 0, 255);
        int gWhite = Math.Clamp((cW + gTermW) >> 8, 0, 255);
        int bWhite = Math.Clamp((cW + bTermW) >> 8, 0, 255);

        Assert.Equal(255, rWhite);
        Assert.Equal(255, gWhite);
        Assert.Equal(255, bWhite);

        // 2. Studio Black: Y=16, U=128, V=128 -> Full RGB (0, 0, 0)
        int uB = 128 - 128;
        int yB = 16 - 16;
        int vB = 128 - 128;
        int rTermB = 459 * vB + 128;
        int gTermB = -55 * uB - 136 * vB + 128;
        int bTermB = 541 * uB + 128;
        int cB = 298 * yB;
        int rBlack = Math.Clamp((cB + rTermB) >> 8, 0, 255);
        int gBlack = Math.Clamp((cB + gTermB) >> 8, 0, 255);
        int bBlack = Math.Clamp((cB + bTermB) >> 8, 0, 255);

        Assert.Equal(0, rBlack);
        Assert.Equal(0, gBlack);
        Assert.Equal(0, bBlack);
    }
}
