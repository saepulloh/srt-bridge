using GDI.Core.Models;
using GDI.Encoder;
using GDI.SRT.Engine;
using Xunit;

namespace GDI.Tests;

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
    public void PortConflictValidator_DuplicateDeckLinkDevice_ReportsConflict()
    {
        var existing = new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Name = "STUDIO IN",
            Direction = ChannelDirection.Encoder,
            VideoDevice = "Decklink Video Capture (3)"
        };

        var validator = new GDI.Hardware.Validation.PortConflictValidator(new[] { existing });
        var report = validator.Validate(new ChannelConfig
        {
            Id = Guid.NewGuid(),
            Name = "NEW CHANNEL",
            Direction = ChannelDirection.Encoder,
            VideoDevice = "Decklink Video Capture (3)"
        });

        Assert.False(report.IsValid);
        Assert.Contains("already assigned", report.Errors.First());
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
}
