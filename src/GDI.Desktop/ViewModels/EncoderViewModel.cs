using System.Collections.ObjectModel;
using GDI.Core.Models;

namespace GDI.Desktop.ViewModels;

public sealed class EncoderViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<ChannelCardViewModel> Encoders { get; } = [];

    public AsyncRelayCommand AddEncoderCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public EncoderViewModel(MainViewModel main)
    {
        _main = main;
        AddEncoderCommand = new AsyncRelayCommand(() => _main.OpenChannelModalAsync(null));
        RefreshCommand = new AsyncRelayCommand(RefreshChannelsAsync);
    }

    public Task RefreshChannelsAsync()
    {
        var all = _main.ChannelManager.GetAll().Where(c => c.Direction == ChannelDirection.Encoder).ToList();
        Encoders.Clear();
        foreach (var ch in all)
        {
            Encoders.Add(new ChannelCardViewModel(_main, ch));
        }
        return Task.CompletedTask;
    }

    public void UpdateTelemetry(IReadOnlyList<ChannelConfig> all)
    {
        foreach (var enc in Encoders)
        {
            var isLive = _main.ChannelManager.IsRunning(enc.Id);
            var m = _main.ChannelManager.GetMetrics(enc.Id);
            enc.IsStreaming = isLive;
            enc.StateBadge = m.State;
            enc.LastError = m.LastError;
            enc.BitrateKbps = isLive && m.CurrentBitrateKbps > 0 ? (int)m.CurrentBitrateKbps : enc.Config.VideoBitrateKbps;
            enc.CurrentFps = isLive ? m.CurrentFps : 0;
            enc.DroppedFrames = m.DroppedFrames;

            var db = Math.Max(m.AudioLevelLeftDb, m.AudioLevelRightDb);
            enc.VuLevel = isLive ? Math.Clamp((db + 60.0) / 60.0 * 100.0, 0, 100) : 0;
            enc.RefreshPreview();
        }
    }
}
