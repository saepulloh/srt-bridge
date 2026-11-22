using System.Collections.Concurrent;

namespace GDI.Core.Services;

public sealed class RealtimeVideoFrame
{
    public Guid ChannelId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
    public byte[] Data { get; set; } = [];
    public long FrameNumber { get; set; }
    public long TimestampTicks { get; set; }
    public string PixelFormat { get; set; } = "BGRA32";
}

public interface IRealtimeFrameDistributor
{
    void PushFrame(Guid channelId, int width, int height, byte[] frameData, string pixelFormat = "BGRA32");
    RealtimeVideoFrame? GetLatestFrame(Guid channelId);
    event Action<Guid, RealtimeVideoFrame>? OnFrameAvailable;
}

public sealed class RealtimeFrameDistributor : IRealtimeFrameDistributor
{
    public static RealtimeFrameDistributor Instance { get; } = new();

    private readonly ConcurrentDictionary<Guid, RealtimeVideoFrame> _latestFrames = new();
    private readonly ConcurrentDictionary<Guid, long> _frameCounters = new();

    public event Action<Guid, RealtimeVideoFrame>? OnFrameAvailable;

    public void PushFrame(Guid channelId, int width, int height, byte[] frameData, string pixelFormat = "BGRA32")
    {
        long fn = _frameCounters.AddOrUpdate(channelId, 1, (_, current) => current + 1);
        int stride = width * (pixelFormat == "BGRA32" ? 4 : 2);

        var frame = new RealtimeVideoFrame
        {
            ChannelId = channelId,
            Width = width,
            Height = height,
            Stride = stride,
            Data = frameData,
            FrameNumber = fn,
            TimestampTicks = DateTime.UtcNow.Ticks,
            PixelFormat = pixelFormat
        };

        _latestFrames[channelId] = frame;
        try
        {
            OnFrameAvailable?.Invoke(channelId, frame);
        }
        catch { }
    }

    public RealtimeVideoFrame? GetLatestFrame(Guid channelId)
    {
        return _latestFrames.TryGetValue(channelId, out var f) ? f : null;
    }
}
