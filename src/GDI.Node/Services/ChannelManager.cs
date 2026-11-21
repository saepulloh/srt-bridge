using System.Collections.Concurrent;
using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Storage;
using GDI.Decoder;
using GDI.Encoder;

namespace GDI.Node.Services;

public interface IChannelManager
{
    IReadOnlyList<ChannelConfig> GetAll();
    ChannelConfig? Get(Guid id);
    Task<ChannelConfig> AddOrUpdateAsync(ChannelConfig channel);
    Task<bool> DeleteAsync(Guid id);
    Task<(bool Success, string Message)> StartChannelAsync(Guid id);
    Task StopChannelAsync(Guid id);
    Task<(bool Success, string Message)> RestartChannelAsync(Guid id);
    ChannelMetrics GetMetrics(Guid id);
    bool IsRunning(Guid id);
}

public sealed class ChannelManager : IChannelManager, IDisposable
{
    private readonly IConfigurationStore _configStore;
    private readonly IEncoderEngine _encoderEngine;
    private readonly IDecoderEngine _decoderEngine;
    private readonly ISecretVault _vault;
    private readonly BroadcastEventBus _eventBus;
    private readonly List<ChannelConfig> _channels = [];
    private readonly ConcurrentDictionary<Guid, byte> _manualStops = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PeriodicTimer _watchdogTimer = new(TimeSpan.FromSeconds(5));
    private readonly CancellationTokenSource _cts = new();
    private const string ConfigFileName = "channels.json";

    public ChannelManager(
        IConfigurationStore configStore,
        IEncoderEngine encoderEngine,
        IDecoderEngine decoderEngine,
        ISecretVault vault,
        BroadcastEventBus eventBus)
    {
        _configStore = configStore;
        _encoderEngine = encoderEngine;
        _decoderEngine = decoderEngine;
        _vault = vault;
        _eventBus = eventBus;

        LoadChannels();
        _ = RunWatchdogLoopAsync(_cts.Token);
    }

    private void LoadChannels()
    {
        var loaded = Task.Run(async () => await _configStore.LoadAsync(ConfigFileName, new List<ChannelConfig>()).ConfigureAwait(false)).GetAwaiter().GetResult();
        _channels.Clear();
        foreach (var c in loaded)
        {
            // Restore secure passphrase from vault if stored
            var secretKey = $"channel_pass_{c.Id:N}";
            if (_vault.HasSecret(secretKey))
            {
                c.Passphrase = _vault.Unprotect(secretKey);
            }
            _channels.Add(c);
        }
    }

    public IReadOnlyList<ChannelConfig> GetAll()
    {
        lock (_channels)
        {
            return _channels.Select(c =>
            {
                var clone = c.Clone();
                clone.RuntimeState = IsRunning(clone.Id) ? (clone.Direction == ChannelDirection.Encoder ? "STREAMING" : "RECEIVING") : "STOPPED";
                return clone;
            }).ToList();
        }
    }

    public ChannelConfig? Get(Guid id)
    {
        lock (_channels)
        {
            var found = _channels.FirstOrDefault(x => x.Id == id);
            if (found == null) return null;
            var clone = found.Clone();
            clone.RuntimeState = IsRunning(clone.Id) ? (clone.Direction == ChannelDirection.Encoder ? "STREAMING" : "RECEIVING") : "STOPPED";
            return clone;
        }
    }

    public async Task<ChannelConfig> AddOrUpdateAsync(ChannelConfig channel)
    {
        await _gate.WaitAsync();
        try
        {
            // Save passphrase to secure DPAPI vault and remove raw plaintext from stored JSON
            if (!string.IsNullOrWhiteSpace(channel.Passphrase))
            {
                _vault.Protect($"channel_pass_{channel.Id:N}", channel.Passphrase);
            }

            lock (_channels)
            {
                var idx = _channels.FindIndex(x => x.Id == channel.Id);
                if (idx >= 0)
                {
                    _channels[idx] = channel.Clone();
                }
                else
                {
                    _channels.Add(channel.Clone());
                }
            }

            // Persist sanitized channel config list
            var forStorage = GetAll().Select(c =>
            {
                var copy = c.Clone();
                copy.Passphrase = null; // Don't persist plaintext secrets
                return copy;
            }).ToList();

            await _configStore.SaveAsync(ConfigFileName, forStorage);
            return channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await StopChannelAsync(id);

        await _gate.WaitAsync();
        try
        {
            _vault.Remove($"channel_pass_{id:N}");
            bool removed;
            lock (_channels)
            {
                removed = _channels.RemoveAll(x => x.Id == id) > 0;
            }

            if (removed)
            {
                var forStorage = GetAll().Select(c => { var copy = c.Clone(); copy.Passphrase = null; return copy; }).ToList();
                await _configStore.SaveAsync(ConfigFileName, forStorage);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsRunning(Guid id)
    {
        return _encoderEngine.IsRunning(id) || _decoderEngine.IsRunning(id);
    }

    public ChannelMetrics GetMetrics(Guid id)
    {
        var channel = Get(id);
        if (channel == null) return new ChannelMetrics { ChannelId = id, State = "STOPPED" };

        return channel.Direction == ChannelDirection.Encoder
            ? _encoderEngine.GetMetrics(id)
            : _decoderEngine.GetMetrics(id);
    }

    public async Task<(bool Success, string Message)> StartChannelAsync(Guid id)
    {
        var channel = Get(id);
        if (channel == null) return (false, "Channel not found.");

        _manualStops.TryRemove(id, out _);

        // Populate passphrase from vault if not present
        if (string.IsNullOrWhiteSpace(channel.Passphrase))
        {
            channel.Passphrase = _vault.Unprotect($"channel_pass_{channel.Id:N}");
        }

        // Prevent duplicate usage of Blackmagic DeckLink devices across active channels
        if (channel.Direction == ChannelDirection.Encoder && !string.IsNullOrWhiteSpace(channel.VideoDevice))
        {
            bool isDecklink = channel.VideoDevice.Contains("Decklink", StringComparison.OrdinalIgnoreCase) ||
                              channel.VideoDevice.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase);

            if (isDecklink)
            {
                var activeWithSameDev = GetAll().FirstOrDefault(c =>
                    c.Id != channel.Id &&
                    IsRunning(c.Id) &&
                    c.Direction == ChannelDirection.Encoder &&
                    !string.IsNullOrWhiteSpace(c.VideoDevice) &&
                    c.VideoDevice.Equals(channel.VideoDevice, StringComparison.OrdinalIgnoreCase));

                if (activeWithSameDev != null)
                {
                    var msg = $"DeckLink port '{channel.VideoDevice}' is already in use by active channel '{activeWithSameDev.Name}'. Operation aborted to prevent hardware conflict.";
                    _eventBus.Publish($"[CONFLICT] {msg}");
                    return (false, msg);
                }
            }
        }

        if (channel.Direction == ChannelDirection.Encoder)
        {
            return await _encoderEngine.StartAsync(channel);
        }
        else
        {
            return await _decoderEngine.StartAsync(channel);
        }
    }

    public async Task StopChannelAsync(Guid id)
    {
        _manualStops[id] = 1;
        await _encoderEngine.StopAsync(id);
        await _decoderEngine.StopAsync(id);
    }

    public async Task<(bool Success, string Message)> RestartChannelAsync(Guid id)
    {
        await StopChannelAsync(id);
        await Task.Delay(500);
        return await StartChannelAsync(id);
    }

    private async Task RunWatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && await _watchdogTimer.WaitForNextTickAsync(ct))
        {
            try
            {
                var all = GetAll();
                foreach (var channel in all.Where(c => c.Enabled && c.AutoStart))
                {
                    if (_manualStops.ContainsKey(channel.Id)) continue;
                    if (IsRunning(channel.Id)) continue;

                    // Automatic isolated recovery for this specific channel
                    await StartChannelAsync(channel.Id);
                }
            }
            catch
            {
                // Watchdog must never throw to preserve resilience
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watchdogTimer.Dispose();
        _cts.Dispose();
        _gate.Dispose();
    }
}
