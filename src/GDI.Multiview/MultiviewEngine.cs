using System.Text.Json;
using GDI.Core.Models;

namespace GDI.Multiview;

public enum MultiviewLayout
{
    Single,     // 1 View
    Dual,       // 2 View (Side by side)
    Quad,       // 4 View (2x2)
    Hexa,       // 6 View (1 Large + 5 Small / 2x3)
    Octo,       // 8 View (2 Large + 6 Small / 2x4)
    Nine,       // 9 View (3x3)
    Sixteen,    // 16 View (4x4)
    Custom      // Custom Grid (CustomRows x CustomColumns)
}

public sealed class MultiviewTile
{
    public int TileIndex { get; set; }
    public Guid? AssignedChannelId { get; set; }
    public string ChannelName { get; set; } = "OFF AIR";
    public string TallyState { get; set; } = "OFF"; // OFF, PREVIEW (Green), PROGRAM (Red), LIVE (Amber)
    public string ResolutionFps { get; set; } = "-";
    public double BitrateKbps { get; set; }
    public double SrtRttMs { get; set; }
    public long DroppedFrames { get; set; }
    public float AudioLevelLeftDb { get; set; } = -60f;
    public float AudioLevelRightDb { get; set; } = -60f;
    public bool IsLive { get; set; }
    public string? PreviewImagePath { get; set; }
    public bool HasPreviewImage { get; set; }
}

public interface IMultiviewEngine
{
    MultiviewLayout CurrentLayout { get; set; }
    int CustomRows { get; set; }
    int CustomColumns { get; set; }
    int GetTileCount();
    IReadOnlyList<MultiviewTile> GetTiles(IReadOnlyList<ChannelConfig> channels, Func<Guid, ChannelMetrics> metricsProvider);
    void AssignChannelToTile(int tileIndex, Guid? channelId);
    Guid? GetAssignedChannel(int tileIndex);
    IReadOnlyDictionary<int, Guid?> GetAllAssignments();
    void ClearAssignments();
    void SaveConfig();
    void LoadConfig();
}

public sealed class MultiviewConfigModel
{
    public MultiviewLayout Layout { get; set; } = MultiviewLayout.Quad;
    public int CustomRows { get; set; } = 2;
    public int CustomColumns { get; set; } = 3;
    public Dictionary<int, string?> Assignments { get; set; } = new();
}

public sealed class MultiviewEngine : IMultiviewEngine
{
    private readonly Dictionary<int, Guid?> _manualAssignments = new();
    private readonly string _configFilePath;

    public MultiviewLayout CurrentLayout { get; set; } = MultiviewLayout.Quad;
    public int CustomRows { get; set; } = 2;
    public int CustomColumns { get; set; } = 3;

    public MultiviewEngine()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "config");
        try { Directory.CreateDirectory(dir); } catch { }
        _configFilePath = Path.Combine(dir, "multiview.json");
        LoadConfig();
    }

    public int GetTileCount() => CurrentLayout switch
    {
        MultiviewLayout.Single => 1,
        MultiviewLayout.Dual => 2,
        MultiviewLayout.Quad => 4,
        MultiviewLayout.Hexa => 6,
        MultiviewLayout.Octo => 8,
        MultiviewLayout.Nine => 9,
        MultiviewLayout.Sixteen => 16,
        MultiviewLayout.Custom => Math.Clamp(CustomRows * CustomColumns, 1, 36),
        _ => 4
    };

    public void AssignChannelToTile(int tileIndex, Guid? channelId)
    {
        lock (_manualAssignments)
        {
            if (channelId.HasValue)
            {
                _manualAssignments[tileIndex] = channelId;
            }
            else
            {
                _manualAssignments.Remove(tileIndex);
            }
        }
    }

    public Guid? GetAssignedChannel(int tileIndex)
    {
        lock (_manualAssignments)
        {
            return _manualAssignments.TryGetValue(tileIndex, out var id) ? id : null;
        }
    }

    public IReadOnlyDictionary<int, Guid?> GetAllAssignments()
    {
        lock (_manualAssignments)
        {
            return new Dictionary<int, Guid?>(_manualAssignments);
        }
    }

    public void ClearAssignments()
    {
        lock (_manualAssignments)
        {
            _manualAssignments.Clear();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var model = new MultiviewConfigModel
            {
                Layout = CurrentLayout,
                CustomRows = CustomRows,
                CustomColumns = CustomColumns,
                Assignments = _manualAssignments.ToDictionary(k => k.Key, v => v.Value?.ToString("D"))
            };
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch { }
    }

    public void LoadConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath)) return;
            var json = File.ReadAllText(_configFilePath);
            var model = JsonSerializer.Deserialize<MultiviewConfigModel>(json);
            if (model != null)
            {
                CurrentLayout = model.Layout;
                if (model.CustomRows >= 1 && model.CustomRows <= 6) CustomRows = model.CustomRows;
                if (model.CustomColumns >= 1 && model.CustomColumns <= 6) CustomColumns = model.CustomColumns;

                lock (_manualAssignments)
                {
                    _manualAssignments.Clear();
                    foreach (var kvp in model.Assignments)
                    {
                        if (Guid.TryParse(kvp.Value, out var guid))
                        {
                            _manualAssignments[kvp.Key] = guid;
                        }
                    }
                }
            }
        }
        catch { }
    }

    public IReadOnlyList<MultiviewTile> GetTiles(IReadOnlyList<ChannelConfig> channels, Func<Guid, ChannelMetrics> metricsProvider)
    {
        var count = GetTileCount();
        var tiles = new List<MultiviewTile>(count);

        for (var i = 0; i < count; i++)
        {
            var tile = new MultiviewTile { TileIndex = i };

            // Determine channel: either explicit assignment or channel in index slot
            Guid? channelId;
            lock (_manualAssignments)
            {
                channelId = _manualAssignments.TryGetValue(i, out var assigned)
                    ? assigned
                    : (i < channels.Count ? channels[i].Id : null);
            }

            if (channelId.HasValue && channels.FirstOrDefault(c => c.Id == channelId.Value) is { } ch)
            {
                var m = metricsProvider(ch.Id);
                tile.AssignedChannelId = ch.Id;
                tile.ChannelName = ch.Name;
                tile.IsLive = m.State is "STREAMING" or "RECEIVING";
                tile.TallyState = tile.IsLive ? "PROGRAM" : "OFF";
                tile.ResolutionFps = $"{ch.Width}x{ch.Height} @ {ch.Fps}p";
                tile.BitrateKbps = m.CurrentBitrateKbps > 0 ? m.CurrentBitrateKbps : ch.VideoBitrateKbps;
                tile.SrtRttMs = m.SrtRttMs;
                tile.DroppedFrames = m.DroppedFrames;
                tile.AudioLevelLeftDb = m.AudioLevelLeftDb;
                tile.AudioLevelRightDb = m.AudioLevelRightDb;

                var previewDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "previews");
                var previewPath = Path.Combine(previewDir, $"{ch.Id:N}.jpg");
                tile.PreviewImagePath = previewPath;
                tile.HasPreviewImage = File.Exists(previewPath);
            }

            tiles.Add(tile);
        }

        return tiles;
    }
}
