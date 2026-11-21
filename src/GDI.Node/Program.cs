using System.Security.Cryptography;
using GDI.Core.Events;
using GDI.Core.Models;
using GDI.Core.Security;
using GDI.Core.Services;
using GDI.Core.Storage;
using GDI.Decoder;
using GDI.Encoder;
using GDI.Hardware;
using GDI.Hardware.Validation;
using GDI.Multiview;
using GDI.Network;
using GDI.Node.Services;
using GDI.ProgramReturn;

// Determine wwwroot location (portable & self-contained friendly)
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(webRoot))
{
    var alt = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    if (Directory.Exists(alt)) webRoot = alt;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(webRoot) ? webRoot : null
});

// Enable CORS for LAN, VPN, and localhost access
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configuration & Environment Variables
builder.Configuration.AddEnvironmentVariables("GDI_");
var listenUrl = builder.Configuration["Urls"] ?? $"http://{builder.Configuration["WebRemoteBindAddress"] ?? "0.0.0.0"}:{builder.Configuration["WebRemotePort"] ?? "8787"}";
builder.WebHost.UseUrls(listenUrl);

// Resolve FFmpeg & FFplay binary paths
var ffmpegPath = Environment.GetEnvironmentVariable("GDI_FFMPEG_PATH")
    ?? builder.Configuration["FfmpegPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin", "ffmpeg.exe");

if (!File.Exists(ffmpegPath))
{
    var systemFfmpeg = Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg", "bin", "ffmpeg.exe");
    if (File.Exists(systemFfmpeg)) ffmpegPath = systemFfmpeg;
    else ffmpegPath = "ffmpeg";
}

var ffplayPath = Environment.GetEnvironmentVariable("GDI_FFPLAY_PATH")
    ?? builder.Configuration["FfplayPath"]
    ?? Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffplay.exe");

if (!File.Exists(ffplayPath)) ffplayPath = "ffplay";

// Register Core broadcast services
builder.Services.AddSingleton<IConfigurationStore, JsonConfigurationStore>(_ =>
{
    var customConfigDir = builder.Configuration["ConfigDir"];
    return new JsonConfigurationStore(customConfigDir);
});

builder.Services.AddSingleton<ISecretVault, DpapiSecretVault>(_ =>
{
    var customVaultPath = builder.Configuration["VaultPath"];
    return new DpapiSecretVault(customVaultPath);
});

builder.Services.AddSingleton<BroadcastEventBus>();
builder.Services.AddSingleton<ISystemMonitor, SystemMonitor>();
builder.Services.AddSingleton<INetworkManager, NetworkManager>();
builder.Services.AddSingleton(new HardwareRegistry(ffmpegPath));
builder.Services.AddSingleton<IMultiviewEngine, MultiviewEngine>();

builder.Services.AddSingleton<IEncoderEngine>(sp =>
    new EncoderEngine(ffmpegPath, sp.GetRequiredService<BroadcastEventBus>()));

builder.Services.AddSingleton<IDecoderEngine>(sp =>
    new DecoderEngine(ffplayPath, ffmpegPath, sp.GetRequiredService<BroadcastEventBus>()));

builder.Services.AddSingleton<IProgramReturnEngine>(sp =>
    new ProgramReturnEngine(ffmpegPath, ffplayPath, sp.GetRequiredService<IConfigurationStore>(), sp.GetRequiredService<ISecretVault>()));

builder.Services.AddSingleton<IChannelManager, ChannelManager>();

var app = builder.Build();

app.UseCors();

// Prevent browser caching for Web Remote HTML interface
app.Use(async (context, next) =>
{
    var p = context.Request.Path.Value ?? "";
    if (p == "" || p == "/" || p.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Security Middleware: API Token authentication with constant-time verification
app.Use(async (context, next) =>
{
    // Handle CORS preflight directly
    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return;
    }

    // Public endpoints that do not require token
    var path = context.Request.Path.Value ?? "";
    if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/auth/status", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var configuredToken = app.Configuration["ApiToken"] ?? Environment.GetEnvironmentVariable("GDI_API_TOKEN");
        var vault = context.RequestServices.GetRequiredService<ISecretVault>();
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            configuredToken = vault.Unprotect("node_api_token");
        }

        // Auto-seed default token on initial deployment so the node is immediately usable
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            configuredToken = "GDI-SRT-2026";
            vault.Protect("node_api_token", configuredToken);
        }

        var suppliedToken = context.Request.Headers["X-GDI-Token"].FirstOrDefault()
            ?? context.Request.Headers["X-API-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(suppliedToken) && context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerVal = authHeader.ToString();
            if (headerVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                suppliedToken = headerVal.Substring(7).Trim();
            }
        }

        if (string.IsNullOrEmpty(suppliedToken) || !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(configuredToken),
            System.Text.Encoding.UTF8.GetBytes(suppliedToken)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid API token.");
            return;
        }
    }
    await next();
});

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new
{
    name = "GDI SRT Embedded Broadcast Node",
    version = "1.0.0",
    platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Ubuntu Linux" : "Other",
    status = "READY",
    timestamp = DateTimeOffset.UtcNow
}));

// Auth status endpoint (unauthenticated)
app.MapGet("/api/auth/status", (ISecretVault vault) =>
{
    var hasToken = vault.HasSecret("node_api_token");
    return Results.Ok(new { ready = true, defaultToken = "GDI-SRT-2026", hasToken });
});

// Auth verification endpoint (token protected)
app.MapGet("/api/auth/verify", () => Results.Ok(new { valid = true, version = "1.0.0" }));

// System telemetry endpoint
app.MapGet("/api/system", async (ISystemMonitor monitor, IChannelManager channels) =>
{
    var m = await monitor.GetMetricsAsync();
    var allChannels = channels.GetAll();
    m.ActiveChannels = allChannels.Count(c => channels.IsRunning(c.Id));
    m.ActiveEncoders = allChannels.Count(c => c.Direction == ChannelDirection.Encoder && channels.IsRunning(c.Id));
    m.ActiveDecoders = allChannels.Count(c => c.Direction == ChannelDirection.Decoder && channels.IsRunning(c.Id));
    m.ActiveSrtConnections = m.ActiveChannels;
    return Results.Ok(m);
});

// Hardware detection report
app.MapGet("/api/hardware", async (HardwareRegistry registry, ISystemMonitor monitor) =>
{
    var devices = await registry.DiscoverAllDevicesAsync();
    var sys = await monitor.GetMetricsAsync();
    return Results.Ok(new
    {
        scannedAt = DateTimeOffset.UtcNow,
        devices,
        gpu = new { available = sys.GpuAvailable, model = sys.GpuModel, vramGiB = sys.VramTotalGiB, tempC = sys.GpuTemperatureC },
        providers = registry.Providers.Select(p => new { vendor = p.VendorName, status = p.VerificationStatus.ToString() })
    });
});

app.MapGet("/api/hardware/devices", async (HardwareRegistry registry) =>
    Results.Ok(await registry.DiscoverAllDevicesAsync()));

app.MapGet("/api/hardware/inputs", async (HardwareRegistry registry, IChannelManager manager) =>
    Results.Ok(await registry.GetVideoInputChoicesAsync(manager.GetAll())));

// Channel endpoints
app.MapGet("/api/channels", (IChannelManager manager) =>
{
    var channels = manager.GetAll().Select(c =>
    {
        var copy = c.Clone();
        copy.Passphrase = SecretRedactor.Mask(copy.Passphrase);
        var metrics = manager.GetMetrics(c.Id);
        return new { config = copy, metrics };
    });
    return Results.Ok(channels);
});

app.MapPost("/api/channels", async (ChannelConfig channel, IChannelManager manager, IConfigurationStore store) =>
{
    var settings = await store.LoadAsync("node-settings.json", new NodeSettings());
    if (manager.GetAll().Count >= settings.ChannelLimit)
    {
        return Results.BadRequest($"Channel limit reached ({settings.ChannelLimit}). Increase limit in Node Settings.");
    }

    ValidateChannel(channel, manager);
    var saved = await manager.AddOrUpdateAsync(channel);
    var redacted = saved.Clone();
    redacted.Passphrase = SecretRedactor.Mask(redacted.Passphrase);
    return Results.Created($"/api/channels/{saved.Id}", redacted);
});

app.MapPut("/api/channels/{id:guid}", async (Guid id, ChannelConfig channel, IChannelManager manager) =>
{
    channel.Id = id;
    ValidateChannel(channel, manager);
    var wasRunning = manager.IsRunning(id);
    if (wasRunning) await manager.StopChannelAsync(id);
    var saved = await manager.AddOrUpdateAsync(channel);
    if (wasRunning) await manager.StartChannelAsync(id);
    var redacted = saved.Clone();
    redacted.Passphrase = SecretRedactor.Mask(redacted.Passphrase);
    return Results.Ok(redacted);
});

app.MapDelete("/api/channels/{id:guid}", async (Guid id, IChannelManager manager) =>
{
    var removed = await manager.DeleteAsync(id);
    return removed ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/channels/{id:guid}/start", async (Guid id, IChannelManager manager) =>
{
    var result = await manager.StartChannelAsync(id);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/channels/{id:guid}/stop", async (Guid id, IChannelManager manager) =>
{
    await manager.StopChannelAsync(id);
    return Results.Ok(new { success = true, message = "Channel stopped." });
});

app.MapPost("/api/channels/{id:guid}/restart", async (Guid id, IChannelManager manager) =>
{
    var result = await manager.RestartChannelAsync(id);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/channels/{id:guid}/metrics", (Guid id, IChannelManager manager) =>
    Results.Ok(manager.GetMetrics(id)));

app.MapGet("/api/channels/{id:guid}/preview", (Guid id) =>
{
    var previewDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "previews");
    var previewFile = Path.Combine(previewDir, $"{id:N}.jpg");
    if (File.Exists(previewFile))
    {
        try
        {
            var bytes = File.ReadAllBytes(previewFile);
            return Results.File(bytes, "image/jpeg");
        }
        catch { }
    }
    return Results.NotFound();
});

// Multiview endpoints
app.MapGet("/api/multiview", (IMultiviewEngine multiview, IChannelManager channels) =>
{
    var allChannels = channels.GetAll();
    var tiles = multiview.GetTiles(allChannels, id => channels.GetMetrics(id));
    return Results.Ok(new { layout = multiview.CurrentLayout.ToString(), tiles });
});

app.MapPut("/api/multiview/layout", (string layout, IMultiviewEngine multiview) =>
{
    if (Enum.TryParse<MultiviewLayout>(layout, true, out var parsed))
    {
        multiview.CurrentLayout = parsed;
        return Results.Ok(new { layout = multiview.CurrentLayout.ToString() });
    }
    return Results.BadRequest("Invalid layout name. Supported: Single, Dual, Quad, Hexa, Octo, Nine, Sixteen");
});

// Program Return endpoints
app.MapGet("/api/return", async (IProgramReturnEngine returnEngine, IConfigurationStore store) =>
{
    var config = await store.LoadAsync("program-return.json", new ProgramReturnConfig());
    config.Passphrase = SecretRedactor.Mask(config.Passphrase);
    return Results.Ok(new { config, status = returnEngine.GetStatus() });
});

app.MapPut("/api/return", async (ProgramReturnConfig incoming, IProgramReturnEngine returnEngine) =>
{
    if (incoming.Role is not ("studio-send" or "venue-receive"))
        return Results.BadRequest("Role must be 'studio-send' or 'venue-receive'.");
    if (incoming.Port is < 1 or > 65535) return Results.BadRequest("Invalid UDP port (1-65535).");

    await returnEngine.StopAsync();
    var result = await returnEngine.StartAsync(incoming);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/return/start", async (IProgramReturnEngine returnEngine, IConfigurationStore store) =>
{
    var config = await store.LoadAsync("program-return.json", new ProgramReturnConfig());
    var result = await returnEngine.StartAsync(config);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/return/stop", async (IProgramReturnEngine returnEngine) =>
{
    await returnEngine.StopAsync();
    return Results.Ok(new { success = true, status = returnEngine.GetStatus() });
});

// Network endpoints
app.MapGet("/api/network", (INetworkManager net) => Results.Ok(net.GetAdapters()));

// Hardware I/O Assignment endpoints
app.MapGet("/api/io", async (IConfigurationStore store) =>
    Results.Ok(await store.LoadAsync("io-routing.json", new IoRoutingConfig())));

app.MapPut("/api/io", async (IoRoutingConfig incoming, IConfigurationStore store, IChannelManager channels) =>
{
    var validation = PortConflictValidator.Validate(incoming, channels.GetAll());
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { success = false, errors = validation.Errors });
    }

    await store.SaveAsync("io-routing.json", incoming);
    return Results.Ok(new { success = true, config = incoming });
});

app.MapGet("/api/io/discovery", async (HardwareRegistry registry) =>
    Results.Ok(await registry.DiscoverAllDevicesAsync()));

// Settings endpoints
app.MapGet("/api/settings", async (IConfigurationStore store) =>
    Results.Ok(await store.LoadAsync("node-settings.json", new NodeSettings())));

app.MapPut("/api/settings", async (NodeSettings incoming, IConfigurationStore store) =>
{
    if (incoming.ChannelLimit is < 1 or > 32) return Results.BadRequest("Channel limit must be between 1 and 32.");
    await store.SaveAsync("node-settings.json", incoming);
    return Results.Ok(incoming);
});

// Seed default broadcast channels if empty or auto-heal USB Video
try
{
    using var scope = app.Services.CreateScope();
    var chMgr = scope.ServiceProvider.GetRequiredService<IChannelManager>();
    var hwReg = scope.ServiceProvider.GetRequiredService<HardwareRegistry>();
    var existingChannels = chMgr.GetAll();
    var choices = await hwReg.GetVideoInputChoicesAsync();
    var bmdInputs = choices.Where(c => c.Vendor.Equals("Blackmagic Design", StringComparison.OrdinalIgnoreCase)).ToList();

    for (int i = 0; i < existingChannels.Count; i++)
    {
        var ch = existingChannels[i];
        if (ch.VideoDevice == "USB Video" || string.IsNullOrWhiteSpace(ch.VideoDevice))
        {
            var portNum = i + 1;
            if (bmdInputs.Count >= portNum)
            {
                ch.VideoDevice = bmdInputs[portNum - 1].DeviceName;
                ch.AudioDevice = bmdInputs[portNum - 1].DeviceName.Replace("Video", "Audio");
                ch.InputBackend = "dshow";
            }
            else if (bmdInputs.Count > 0)
            {
                ch.VideoDevice = bmdInputs[0].DeviceName;
                ch.AudioDevice = bmdInputs[0].DeviceName.Replace("Video", "Audio");
                ch.InputBackend = "dshow";
            }
            else
            {
                ch.VideoDevice = "testsrc2";
                ch.InputBackend = "lavfi";
            }
            await chMgr.AddOrUpdateAsync(ch);
        }
    }

    if (existingChannels.Count == 0)
    {
        var ch1Device = bmdInputs.Count >= 1 ? bmdInputs[0].DeviceName : "testsrc2";
        var ch1Backend = bmdInputs.Count >= 1 ? "dshow" : "lavfi";
        var ch1Audio = bmdInputs.Count >= 1 ? bmdInputs[0].DeviceName.Replace("Video", "Audio") : null;

        var ch2Device = bmdInputs.Count >= 2 ? bmdInputs[1].DeviceName : (bmdInputs.Count >= 1 ? bmdInputs[0].DeviceName : "testsrc2");
        var ch2Backend = bmdInputs.Count >= 2 ? "dshow" : (bmdInputs.Count >= 1 ? "dshow" : "lavfi");
        var ch2Audio = bmdInputs.Count >= 2 ? bmdInputs[1].DeviceName.Replace("Video", "Audio") : null;

        var ch3Device = bmdInputs.Count >= 3 ? bmdInputs[2].DeviceName : (bmdInputs.Count >= 1 ? bmdInputs[0].DeviceName : "testsrc2");
        var ch3Backend = bmdInputs.Count >= 3 ? "dshow" : (bmdInputs.Count >= 1 ? "dshow" : "lavfi");
        var ch3Audio = bmdInputs.Count >= 3 ? bmdInputs[2].DeviceName.Replace("Video", "Audio") : null;

        await chMgr.AddOrUpdateAsync(new ChannelConfig { Name = "PGM 01 — MAIN", Port = 9001, VideoBitrateKbps = 8000, VideoDevice = ch1Device, InputBackend = ch1Backend, AudioDevice = ch1Audio });
        await chMgr.AddOrUpdateAsync(new ChannelConfig { Name = "PGM 02 — CAM A", Port = 9002, VideoBitrateKbps = 6000, VideoDevice = ch2Device, InputBackend = ch2Backend, AudioDevice = ch2Audio });
        await chMgr.AddOrUpdateAsync(new ChannelConfig { Name = "PGM 03 — CAM B", Port = 9003, VideoBitrateKbps = 6000, VideoDevice = ch3Device, InputBackend = ch3Backend, AudioDevice = ch3Audio });
        await chMgr.AddOrUpdateAsync(new ChannelConfig { Name = "RCV 01 — STUDIO", Direction = ChannelDirection.Decoder, Mode = SrtMode.Listener, Port = 9004 });
    }
}
catch { }

app.Run();

static void ValidateChannel(ChannelConfig c, IChannelManager? manager = null)
{
    if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Channel name cannot be empty.");
    if (c.Port is < 1 or > 65535) throw new ArgumentException("Port must be 1-65535.");
    if (c.LatencyMs is < 20 or > 8000) throw new ArgumentException("Latency must be 20-8000 ms.");
    if (c.VideoBitrateKbps is < 250 or > 100000) throw new ArgumentException("Video bitrate must be 250-100000 kbps.");
    if ((c.Width != 0 || c.Height != 0) && (c.Width is < 160 or > 7680 || c.Height is < 120 or > 4320)) throw new ArgumentException("Invalid resolution (use 0x0 for Auto).");
    if (c.Fps != 0 && c.Fps is < 1 or > 120) throw new ArgumentException("FPS must be 1-120 (use 0 for Auto).");
    if (!string.IsNullOrEmpty(c.Passphrase) && c.Passphrase.Length is < 10 or > 79) throw new ArgumentException("SRT passphrase must be 10-79 characters.");

    if (manager != null && c.Direction == ChannelDirection.Encoder && !string.IsNullOrWhiteSpace(c.VideoDevice))
    {
        bool isDecklink = c.VideoDevice.Contains("Decklink", StringComparison.OrdinalIgnoreCase) ||
                          c.VideoDevice.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase);
        if (isDecklink)
        {
            var conflict = manager.GetAll().FirstOrDefault(x =>
                x.Id != c.Id &&
                !string.IsNullOrWhiteSpace(x.VideoDevice) &&
                x.VideoDevice.Equals(c.VideoDevice, StringComparison.OrdinalIgnoreCase));
            if (conflict != null)
            {
                throw new ArgumentException($"DeckLink device '{c.VideoDevice}' is already in use by channel '{conflict.Name}'. Operation rejected to prevent duplicate hardware usage.");
            }
        }
    }
}
