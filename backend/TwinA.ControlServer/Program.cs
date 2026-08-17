using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using TwinA.ControlServer.Hubs;
using TwinA.ControlServer.Models;
using TwinA.ControlServer.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["TwinA:BindUrl"] ?? "http://127.0.0.1:5055");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4L * 1024 * 1024 * 1024);
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSignalR();
builder.Services.AddSingleton<ControlState>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<SessionMarkerService>();
builder.Services.AddSingleton<DesktopAgentClient>();
builder.Services.AddSingleton<ObsWebSocketClient>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<SystemInfoService>();
builder.Services.AddSingleton<SteamLibraryService>();
builder.Services.AddSingleton<FileWorkspaceService>();
builder.Services.AddSingleton<DevWorkspaceService>();
builder.Services.AddSingleton<SoundboardService>();
builder.Services.AddSingleton<MqttRuntimeService>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddHostedService<SystemTelemetryService>();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { ok = true, service = "TWIN A Control Server", version = "0.5.3", time = DateTimeOffset.UtcNow }));
app.MapGet("/api/state", (ControlState state) => state.Snapshot);
app.MapPost("/api/commands/{command}", async (string command, HttpRequest request, CommandDispatcher dispatcher, CancellationToken ct) =>
{
    using var doc = await ReadJsonBodyAsync(request, ct);
    var result = await dispatcher.DispatchAsync(command, doc.RootElement, ct);
    return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
});

// AUDIO + SOUNDBOARD
app.MapGet("/api/audio/devices", async (DesktopAgentClient agent, CancellationToken ct) =>
{
    var result = await agent.SendAsync("audio.devices.get", new(), ct);
    if (!result.Ok || string.IsNullOrWhiteSpace(result.Data)) return Results.Problem(result.Message, statusCode: 503);
    return Results.Text(result.Data, "application/json");
});
app.MapGet("/api/sounds", (SoundboardService sounds) => Results.Ok(sounds.List()));
app.MapPost("/api/sounds/upload", async (HttpRequest request, string fileName, SoundboardService sounds, CancellationToken ct) =>
{
    var target = await sounds.SaveAsync(fileName, request.Body, ct);
    return Results.Ok(new { ok = true, path = target, sound = sounds.List().FirstOrDefault(s => Path.Combine(sounds.Root, s.FileName).Equals(target, StringComparison.OrdinalIgnoreCase)) });
});
app.MapDelete("/api/sounds/{id}", (string id, SoundboardService sounds) => sounds.Delete(id) ? Results.Ok(new { ok=true }) : Results.BadRequest(new { ok=false }));

// GAMES
app.MapGet("/api/games", (SteamLibraryService steam) => Results.Ok(steam.GetAllGames()));
app.MapGet("/api/games/steam-info", (SteamLibraryService steam) => Results.Ok(new { steamPath = steam.SteamPath }));
app.MapPost("/api/games/custom", async (HttpRequest request, SettingsStore settings, CancellationToken ct) =>
{
    var game = await request.ReadFromJsonAsync<CustomGameConfig>(cancellationToken: ct) ?? new CustomGameConfig();
    game.Id = string.IsNullOrWhiteSpace(game.Id) ? Guid.NewGuid().ToString("N") : game.Id;
    var saved = settings.Update(config =>
    {
        var index = config.CustomGames.FindIndex(g => g.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) config.CustomGames[index] = game; else config.CustomGames.Add(game);
        return game;
    });
    return Results.Ok(saved);
});
app.MapDelete("/api/games/custom/{id}", (string id, SettingsStore settings) =>
{
    var removed = settings.Update(config =>
    {
        var gameRemoved = config.CustomGames.RemoveAll(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (gameRemoved) config.GameProfiles.RemoveAll(p => p.GameId.Equals(id, StringComparison.OrdinalIgnoreCase));
        return gameRemoved;
    });
    return removed ? Results.Ok(new {ok=true}) : Results.NotFound();
});
app.MapGet("/api/games/profiles", (SettingsStore settings) => Results.Ok(settings.Get().GameProfiles));
app.MapPost("/api/games/profile", async (HttpRequest request, SettingsStore settings, CancellationToken ct) =>
{
    var profile = await request.ReadFromJsonAsync<GameProfileConfig>(cancellationToken: ct) ?? new GameProfileConfig();
    if (string.IsNullOrWhiteSpace(profile.GameId)) return Results.BadRequest(new {ok=false,message="GameId is required."});
    var saved = settings.Update(config =>
    {
        var index = config.GameProfiles.FindIndex(p => p.GameId.Equals(profile.GameId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) config.GameProfiles[index] = profile; else config.GameProfiles.Add(profile);
        return profile;
    });
    return Results.Ok(saved);
});
app.MapDelete("/api/games/profile/{gameId}", (string gameId, SettingsStore settings) =>
{
    var removed = settings.Update(config => config.GameProfiles.RemoveAll(p => p.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase)) > 0);
    return removed ? Results.Ok(new {ok=true}) : Results.NotFound();
});

// SYSTEM
app.MapGet("/api/system/details", (SystemInfoService system) => Results.Ok(system.GetDetails()));

// FILES - all ready fixed/removable drives are browsable; protected OS paths are read-only by default.
app.MapGet("/api/files/drives", (FileWorkspaceService files) => Results.Ok(files.GetDrives()));
app.MapGet("/api/files/browse", (string path, FileWorkspaceService files) => Results.Ok(files.Browse(path)));
app.MapGet("/api/files/download", (string path, FileWorkspaceService files) =>
{
    var full = files.ValidateDownload(path);
    return Results.File(full, "application/octet-stream", Path.GetFileName(full), enableRangeProcessing: true);
});
app.MapPost("/api/files/upload", async (HttpRequest request, string directory, string fileName, FileWorkspaceService files, CancellationToken ct) =>
{
    var target = await files.SaveUploadAsync(directory, fileName, request.Body, ct);
    return Results.Ok(new {ok=true,path=target});
});

// DEV
app.MapGet("/api/dev/projects", (DevWorkspaceService dev) => Results.Ok(dev.GetProjects()));
app.MapGet("/api/dev/projects/{id}/status", async (string id, DevWorkspaceService dev, CancellationToken ct) => Results.Ok(await dev.GetStatusAsync(id, ct)));
app.MapPost("/api/dev/projects", async (HttpRequest request, DevWorkspaceService dev, CancellationToken ct) =>
{
    var project = await request.ReadFromJsonAsync<DevProjectConfig>(cancellationToken: ct) ?? new DevProjectConfig();
    return Results.Ok(dev.Upsert(project));
});
app.MapDelete("/api/dev/projects/{id}", (string id, DevWorkspaceService dev) => dev.Delete(id) ? Results.Ok(new {ok=true}) : Results.NotFound());

// FLOWS + SETTINGS
app.MapGet("/api/flows", (SettingsStore settings) => Results.Ok(settings.Get().Flows));
app.MapPost("/api/flows", async (HttpRequest request, SettingsStore settings, CancellationToken ct) =>
{
    var flow = await request.ReadFromJsonAsync<FlowConfig>(cancellationToken: ct) ?? new FlowConfig();
    flow.Id = string.IsNullOrWhiteSpace(flow.Id) ? Guid.NewGuid().ToString("N") : flow.Id;
    var saved = settings.Update(config =>
    {
        var index = config.Flows.FindIndex(f => f.Id.Equals(flow.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) config.Flows[index] = flow; else config.Flows.Add(flow);
        return flow;
    });
    return Results.Ok(saved);
});
app.MapDelete("/api/flows/{id}", (string id, SettingsStore settings) => settings.Update(config => config.Flows.RemoveAll(f => f.Id.Equals(id,StringComparison.OrdinalIgnoreCase))>0) ? Results.Ok(new{ok=true}) : Results.NotFound());
app.MapGet("/api/settings", (SettingsStore settings) => Results.Ok(new
{
    version = "0.5.3",
    settings = settings.Get(),
    configPath = settings.ConfigPath,
    obsPasswordStored = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWINA_OBS_PASSWORD")),
    mqttPasswordStored = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWINA_MQTT_PASSWORD"))
}));
app.MapPut("/api/settings", async (HttpRequest request, SettingsStore settings, CancellationToken ct) =>
{
    var config = await request.ReadFromJsonAsync<AppConfiguration>(cancellationToken: ct) ?? new AppConfiguration();
    return Results.Ok(settings.Replace(config));
});

// IoT: real MQTT authentication/publish/state verification when a broker is configured.
app.MapPost("/api/iot/test", async (MqttRuntimeService mqtt, CancellationToken ct) =>
{
    var result = await mqtt.TestAsync(ct);
    return result.Ok ? Results.Ok(new { ok=true, verified=result.Verified, message=result.Message, data=result.Data })
        : Results.BadRequest(new { ok=false, verified=false, message=result.Message });
});
app.MapGet("/api/iot/states", async (MqttRuntimeService mqtt, CancellationToken ct) => Results.Ok(await mqtt.ReadStatesAsync(ct)));

app.MapHub<StateHub>("/hubs/state");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

static async Task<JsonDocument> ReadJsonBodyAsync(HttpRequest request, CancellationToken ct)
{
    if (request.ContentLength is null or 0) return JsonDocument.Parse("{}");
    return await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
}
