using GDI.Core.Models;
using GDI.Encoder;
using GDI.Hardware;
using GDI.SRT.Engine;
using Xunit;

namespace GDI.Tests;

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("50FB36CD-3063-4B73-BDBB-958087F2D8BA"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkIteratorTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int Next(out IntPtr deckLinkInstance);
}

[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("50FB36CD-30F2-4819-BE5D-05BA2F6072AD"), System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
public interface IDeckLinkTest
{
    [System.Runtime.InteropServices.PreserveSig]
    int GetModelName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.BStr)] out string modelName);
    [System.Runtime.InteropServices.PreserveSig]
    int GetDisplayName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.BStr)] out string displayName);
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
        Assert.Contains("25", args);
        Assert.Contains("-pixel_format", args);
        Assert.Contains("uyvy422", args);
        Assert.Contains("video=Decklink Video Capture (2):audio=Decklink Audio Capture (2)", args);
    }

    [Fact]
    public void EncoderArguments_Output2Preview_ContainsExplicitMjpegAndNoAudio()
    {
        var channel = new ChannelConfig
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Width = 1920,
            Height = 1080,
            Fps = 60,
            VideoBitrateKbps = 6000,
            InputBackend = "lavfi",
            IncludeAudio = true,
            VideoCodec = "h264"
        };

        var args = EncoderEngine.BuildEncoderArguments(channel, "libx264").ToList();

        // Must contain output 1 (mpegts srt) and output 2 (image2 mjpeg preview)
        Assert.Contains("-f", args);
        Assert.Contains("mpegts", args);
        Assert.Contains("-f", args);
        Assert.Contains("image2", args);
        Assert.Contains("mjpeg", args);
        Assert.Contains("-an", args);
        Assert.Contains("-update", args);
        Assert.Contains("1", args);
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
            IncludeAudio = true,
            AudioDevice = "Decklink Audio Capture (3)",
            VideoCodec = "h264"
        };

        // When auto-detected signal is 1080p50 (50 fps)
        var args = EncoderEngine.BuildEncoderArguments(channel, "h264_nvenc", 1920, 1080, 50).ToList();

        Assert.Contains("-video_size", args);
        Assert.Contains("1920x1080", args);
        Assert.Contains("-framerate", args);
        Assert.Contains("50", args);
        Assert.Contains("-pixel_format", args);
        Assert.Contains("uyvy422", args);
        Assert.Contains("video=Decklink Video Capture (3):audio=Decklink Audio Capture (3)", args);
    }

    [Fact]
    public void PortConflictValidator_DuplicateDeckLinkDevice_ReportsConflict()
    {
        var ch1 = new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Name = "CAM 1",
            Direction = ChannelDirection.Encoder,
            VideoDevice = "Decklink Video Capture (3)"
        };
        var ch2 = new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Name = "CAM 2",
            Direction = ChannelDirection.Encoder,
            VideoDevice = "Decklink Video Capture (3)"
        };

        var config = new IoRoutingConfig();
        var result = GDI.Hardware.Validation.PortConflictValidator.Validate(config, new[] { ch1, ch2 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate DeckLink hardware assignment") && e.Contains("Decklink Video Capture (3)"));
    }

    [Fact]
    public async Task HardwareRegistry_InUseDeckLinkDevices_MarkedCorrectly()
    {
        var existingChannel = new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Name = "CAM 1",
            Direction = ChannelDirection.Encoder,
            VideoDevice = "Decklink Video Capture"
        };

        var reg = new HardwareRegistry("ffmpeg");
        var choices = await reg.GetVideoInputChoicesAsync(new[] { existingChannel });

        var inUseDevice = choices.FirstOrDefault(c => c.DeviceName.Equals("Decklink Video Capture", StringComparison.OrdinalIgnoreCase));
        if (inUseDevice != null)
        {
            Assert.True(inUseDevice.IsInUse);
            Assert.Equal("CAM 1", inUseDevice.UsedByChannel);
            Assert.Contains("IN USE", inUseDevice.DisplayName);
        }
    }

    [Fact]
    public void DeckLink_VideoRender_Port3_InstantiatesSuccessfully()
    {
        if (!OperatingSystem.IsWindows()) return;
        var clsid = new Guid("4354ED19-BDE1-4083-9BE7-991AF1F0B527"); // Decklink Video Render (3)
        var t = Type.GetTypeFromCLSID(clsid);
        Assert.NotNull(t);
        var filter = Activator.CreateInstance(t) as DirectShowLib.IBaseFilter;
        Assert.NotNull(filter);
        DirectShowLib.FilterInfo info;
        int hr = filter.QueryFilterInfo(out info);
        Assert.Equal(0, hr);

        hr = filter.EnumPins(out var enumPins);
        Assert.Equal(0, hr);
        var pins = new DirectShowLib.IPin[1];
        while (enumPins.Next(1, pins, IntPtr.Zero) == 0 && pins[0] != null)
        {
            DirectShowLib.PinInfo pinInfo;
            pins[0].QueryPinInfo(out pinInfo);
            pins[0].QueryDirection(out var dir);
            System.Diagnostics.Trace.WriteLine($"Pin: {pinInfo.name}, Dir: {dir}");
            System.Runtime.InteropServices.Marshal.ReleaseComObject(pins[0]);
        }
        System.Runtime.InteropServices.Marshal.ReleaseComObject(enumPins);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(filter);
    }

    [Fact]
    public void DeckLink_Api_Classes_Instantiate()
    {
        if (!OperatingSystem.IsWindows()) return;
        var t1 = Type.GetTypeFromCLSID(new Guid("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676"));
        Assert.NotNull(t1);
        var obj1 = Activator.CreateInstance(t1);
        Assert.NotNull(obj1);
        if (obj1 is IDeckLinkIteratorTest iter)
        {
            int index = 0;
            while (iter.Next(out var pDeckLink) == 0 && pDeckLink != IntPtr.Zero)
            {
                var dl = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(pDeckLink) as IDeckLinkTest;
                if (dl != null)
                {
                    dl.GetDisplayName(out var name);
                    dl.GetModelName(out var model);
                    System.Diagnostics.Trace.WriteLine($"DeckLink device [{index}]: {name} (Model: {model})");
                }
                System.Runtime.InteropServices.Marshal.Release(pDeckLink);
                index++;
            }
        }
        System.Runtime.InteropServices.Marshal.ReleaseComObject(obj1);

        var t2 = Type.GetTypeFromCLSID(new Guid("22FBFC33-8D07-495C-A5BF-DAB5EA9B82DB"));
        Assert.NotNull(t2);
        var obj2 = Activator.CreateInstance(t2);
        Assert.NotNull(obj2);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(obj2);
    }
}
