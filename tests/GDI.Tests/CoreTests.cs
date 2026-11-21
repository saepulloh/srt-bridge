using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Storage;
using Xunit;

namespace GDI.Tests;

public class CoreTests
{
    [Fact]
    public void SecretVault_ProtectsAndRetrievesSecret()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gdi_vault_test_{Guid.NewGuid():N}.dat");
        try
        {
            var vault = new DpapiSecretVault(tempFile);
            const string key = "test_passphrase";
            const string secret = "BroadCastSuperSecret2026!";

            vault.Protect(key, secret);
            Assert.True(vault.HasSecret(key));

            var retrieved = vault.Unprotect(key);
            Assert.Equal(secret, retrieved);

            vault.Remove(key);
            Assert.False(vault.HasSecret(key));
            Assert.Null(vault.Unprotect(key));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SecretRedactor_RedactsPassphraseAndTokens()
    {
        const string rawUrl = "srt://192.168.1.50:9000?mode=caller&latency=200&passphrase=SuperSecretPassphrase123&pbkeylen=32";
        var redactedUrl = SecretRedactor.RedactUrl(rawUrl);
        Assert.DoesNotContain("SuperSecretPassphrase123", redactedUrl);
        Assert.Contains("passphrase=[REDACTED]", redactedUrl);

        const string rawHeader = "X-GDI-Token: 9f8a8b7c6d5e4f3a2b1c";
        var redactedHeader = SecretRedactor.RedactHeaders(rawHeader);
        Assert.DoesNotContain("9f8a8b7c6d5e4f3a2b1c", redactedHeader);
        Assert.Contains("X-GDI-Token: [REDACTED]", redactedHeader);

        var masked = SecretRedactor.Mask("SuperSecretToken");
        Assert.Equal("Su****en", masked);
    }

    [Fact]
    public async Task JsonConfigurationStore_SavesAndLoads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gdi_store_test_{Guid.NewGuid():N}");
        try
        {
            var store = new JsonConfigurationStore(tempDir);
            var initial = new NodeSettings { ChannelLimit = 8, DefaultLatencyMs = 300, SelectedFont = "Inter" };

            await store.SaveAsync("settings.json", initial);

            var loaded = await store.LoadAsync("settings.json", new NodeSettings());
            Assert.Equal(8, loaded.ChannelLimit);
            Assert.Equal(300, loaded.DefaultLatencyMs);
            Assert.Equal("Inter", loaded.SelectedFont);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BroadcastEventBus_PublishesToSubscribers()
    {
        var bus = new BroadcastEventBus();
        var received = false;
        string? message = null;

        bus.Subscribe<string>(msg =>
        {
            received = true;
            message = msg;
        });

        bus.Publish("CHANNEL_1_STARTED");

        Assert.True(received);
        Assert.Equal("CHANNEL_1_STARTED", message);
    }

    [Theory]
    [InlineData(GDI.Multiview.MultiviewLayout.Single, 1)]
    [InlineData(GDI.Multiview.MultiviewLayout.Dual, 2)]
    [InlineData(GDI.Multiview.MultiviewLayout.Quad, 4)]
    [InlineData(GDI.Multiview.MultiviewLayout.Hexa, 6)]
    [InlineData(GDI.Multiview.MultiviewLayout.Octo, 8)]
    [InlineData(GDI.Multiview.MultiviewLayout.Nine, 9)]
    [InlineData(GDI.Multiview.MultiviewLayout.Sixteen, 16)]
    public void MultiviewEngine_CalculatesCorrectTileCount(GDI.Multiview.MultiviewLayout layout, int expectedTiles)
    {
        var mv = new GDI.Multiview.MultiviewEngine { CurrentLayout = layout };
        Assert.Equal(expectedTiles, mv.GetTileCount());
    }

    [Fact]
    public void MultiviewEngine_CustomLayout_CalculatesCorrectTileCount()
    {
        var mv = new GDI.Multiview.MultiviewEngine
        {
            CurrentLayout = GDI.Multiview.MultiviewLayout.Custom,
            CustomRows = 3,
            CustomColumns = 4
        };
        Assert.Equal(12, mv.GetTileCount());
    }
}
