using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Storage;
using GDI.Decoder;
using GDI.Encoder;
using GDI.Node.Services;
using Xunit;

namespace GDI.Tests;

public class ChannelManagerTests
{
    private sealed class FakeVault : ISecretVault
    {
        private readonly Dictionary<string, string> _dict = new();
        public void Protect(string key, string plainSecret) => _dict[key] = plainSecret;
        public string? Unprotect(string key) => _dict.TryGetValue(key, out var v) ? v : null;
        public bool HasSecret(string key) => _dict.ContainsKey(key);
        public void Remove(string key) => _dict.Remove(key);
    }

    private sealed class FakeEncoderEngine : IEncoderEngine
    {
        private readonly HashSet<Guid> _running = [];
        public bool IsRunning(Guid channelId) => _running.Contains(channelId);
        public Task<(bool Success, string Message)> StartAsync(ChannelConfig channel)
        {
            _running.Add(channel.Id);
            return Task.FromResult((true, "Started"));
        }
        public Task StopAsync(Guid channelId)
        {
            _running.Remove(channelId);
            return Task.CompletedTask;
        }
        public ChannelMetrics GetMetrics(Guid channelId) => new() { ChannelId = channelId };
    }

    private sealed class FakeDecoderEngine : IDecoderEngine
    {
        private readonly HashSet<Guid> _running = [];
        public bool IsRunning(Guid channelId) => _running.Contains(channelId);
        public Task<(bool Success, string Message)> StartAsync(ChannelConfig channel)
        {
            _running.Add(channel.Id);
            return Task.FromResult((true, "Started"));
        }
        public Task StopAsync(Guid channelId)
        {
            _running.Remove(channelId);
            return Task.CompletedTask;
        }
        public ChannelMetrics GetMetrics(Guid channelId) => new() { ChannelId = channelId };
    }

    [Fact]
    public async Task ChannelManager_ProtectsPassphraseAndPersistsChannels()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gdi_chan_test_{Guid.NewGuid():N}");
        try
        {
            var store = new JsonConfigurationStore(tempDir);
            var vault = new FakeVault();
            var eventBus = new BroadcastEventBus();
            var enc = new FakeEncoderEngine();
            var dec = new FakeDecoderEngine();

            var mgr = new ChannelManager(store, enc, dec, vault, eventBus);

            var ch1 = new ChannelConfig
            {
                Name = "PGM 1",
                Direction = ChannelDirection.Encoder,
                Port = 9100,
                Passphrase = "SuperSecretBroadcastPassphrase!"
            };

            await mgr.AddOrUpdateAsync(ch1);

            // Channel in memory has passphrase
            var retrieved = mgr.Get(ch1.Id);
            Assert.NotNull(retrieved);
            Assert.Equal("PGM 1", retrieved.Name);

            // Verify stored JSON file on disk does NOT contain plaintext passphrase
            var savedJson = await File.ReadAllTextAsync(Path.Combine(tempDir, "channels.json"));
            Assert.DoesNotContain("SuperSecretBroadcastPassphrase!", savedJson);

            // Start channel and verify status
            var (success, _) = await mgr.StartChannelAsync(ch1.Id);
            Assert.True(success);
            Assert.True(mgr.IsRunning(ch1.Id));

            // Stop channel
            await mgr.StopChannelAsync(ch1.Id);
            Assert.False(mgr.IsRunning(ch1.Id));

            mgr.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ChannelManager_MultipleChannelsOperateIndependently()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gdi_multi_test_{Guid.NewGuid():N}");
        try
        {
            var store = new JsonConfigurationStore(tempDir);
            var vault = new FakeVault();
            var eventBus = new BroadcastEventBus();
            var enc = new FakeEncoderEngine();
            var dec = new FakeDecoderEngine();

            var mgr = new ChannelManager(store, enc, dec, vault, eventBus);

            var ch1 = new ChannelConfig { Name = "CH 1", Port = 9001 };
            var ch2 = new ChannelConfig { Name = "CH 2", Port = 9002 };
            var ch3 = new ChannelConfig { Name = "CH 3", Port = 9003 };
            var ch4 = new ChannelConfig { Name = "CH 4", Port = 9004 };

            await mgr.AddOrUpdateAsync(ch1);
            await mgr.AddOrUpdateAsync(ch2);
            await mgr.AddOrUpdateAsync(ch3);
            await mgr.AddOrUpdateAsync(ch4);

            await mgr.StartChannelAsync(ch1.Id);
            await mgr.StartChannelAsync(ch2.Id);
            await mgr.StartChannelAsync(ch3.Id);
            await mgr.StartChannelAsync(ch4.Id);

            Assert.True(mgr.IsRunning(ch1.Id));
            Assert.True(mgr.IsRunning(ch2.Id));
            Assert.True(mgr.IsRunning(ch3.Id));
            Assert.True(mgr.IsRunning(ch4.Id));

            // Stopping CH 2 does NOT stop CH 1, 3, or 4
            await mgr.StopChannelAsync(ch2.Id);

            Assert.True(mgr.IsRunning(ch1.Id));
            Assert.False(mgr.IsRunning(ch2.Id));
            Assert.True(mgr.IsRunning(ch3.Id));
            Assert.True(mgr.IsRunning(ch4.Id));

            mgr.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
