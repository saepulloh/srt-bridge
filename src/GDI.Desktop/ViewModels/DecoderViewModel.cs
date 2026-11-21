using System.Collections.ObjectModel;
using GDI.Core.Models;

namespace GDI.Desktop.ViewModels;

public sealed class DecoderViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<ChannelCardViewModel> Decoders { get; } = [];

    public AsyncRelayCommand AddDecoderCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public DecoderViewModel(MainViewModel main)
    {
        _main = main;
        AddDecoderCommand = new AsyncRelayCommand(() => _main.OpenChannelModalAsync(new ChannelConfig
        {
            Name = $"RCV {(_main.ChannelManager.GetAll().Count(c => c.Direction == ChannelDirection.Decoder) + 1):D2}",
            Direction = ChannelDirection.Decoder,
            Mode = SrtMode.Listener,
            Port = 9004 + _main.ChannelManager.GetAll().Count
        }));
        RefreshCommand = new AsyncRelayCommand(RefreshChannelsAsync);
    }

    public Task RefreshChannelsAsync()
    {
        var all = _main.ChannelManager.GetAll().Where(c => c.Direction == ChannelDirection.Decoder).ToList();
        Decoders.Clear();
        foreach (var ch in all)
        {
            Decoders.Add(new ChannelCardViewModel(_main, ch));
        }
        return Task.CompletedTask;
    }

    public void UpdateTelemetry(IReadOnlyList<ChannelConfig> all)
    {
        foreach (var dec in Decoders)
        {
            var isLive = _main.ChannelManager.IsRunning(dec.Id);
            var m = _main.ChannelManager.GetMetrics(dec.Id);
            dec.IsStreaming = isLive;
            dec.StateBadge = m.State;
            dec.LastError = m.LastError;
            dec.BitrateKbps = isLive && m.CurrentBitrateKbps > 0 ? (int)m.CurrentBitrateKbps : dec.Config.VideoBitrateKbps;
            dec.CurrentFps = isLive ? m.CurrentFps : 0;
            dec.DroppedFrames = m.DroppedFrames;

            var db = Math.Max(m.AudioLevelLeftDb, m.AudioLevelRightDb);
            dec.VuLevel = isLive ? Math.Clamp((db + 60.0) / 60.0 * 100.0, 0, 100) : 0;
            dec.RefreshPreview();
        }
    }
}
