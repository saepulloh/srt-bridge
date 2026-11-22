using System.Collections.Concurrent;

namespace GDI.Core.Services;

public sealed class RealtimeVideoFrame
{
    public Guid ChannelId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
    public int BytesPerPixel { get; set; } = 2;
    public int FrameSize => Stride * Height;
    public byte[] Data { get; set; } = [];
    public long FrameNumber { get; set; }
    public long TimestampTicks { get; set; }
    public string PixelFormat { get; set; } = "UYVY";
}

public interface IRealtimeFrameDistributor
{
    void PushFrame(Guid channelId, int width, int height, byte[] frameData, string pixelFormat = "UYVY", int customStride = 0);
    RealtimeVideoFrame? GetLatestFrame(Guid channelId);
    event Action<Guid, RealtimeVideoFrame>? OnFrameAvailable;
}

public sealed class RealtimeFrameDistributor : IRealtimeFrameDistributor
{
    public static RealtimeFrameDistributor Instance { get; } = new();

    private readonly ConcurrentDictionary<Guid, RealtimeVideoFrame> _latestFrames = new();
    private readonly ConcurrentDictionary<Guid, long> _frameCounters = new();

    public event Action<Guid, RealtimeVideoFrame>? OnFrameAvailable;

    public void PushFrame(Guid channelId, int width, int height, byte[] frameData, string pixelFormat = "UYVY", int customStride = 0)
    {
        long fn = _frameCounters.AddOrUpdate(channelId, 1, (_, current) => current + 1);

        bool isRgb32 = pixelFormat.Equals("BGRA32", StringComparison.OrdinalIgnoreCase) ||
                       pixelFormat.Equals("Bgr32", StringComparison.OrdinalIgnoreCase);
        bool isV210 = pixelFormat.Equals("v210", StringComparison.OrdinalIgnoreCase);

        int bytesPerPixel = isRgb32 ? 4 : (isV210 ? 3 : 2);
        int stride = customStride > 0 ? customStride : (isV210 ? ((width + 47) / 48) * 128 : width * bytesPerPixel);

        var frame = new RealtimeVideoFrame
        {
            ChannelId = channelId,
            Width = width,
            Height = height,
            Stride = stride,
            BytesPerPixel = bytesPerPixel,
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
