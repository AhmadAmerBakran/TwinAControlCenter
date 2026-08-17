using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwinA.ControlServer.Services;

public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private readonly string _url;
    private readonly bool _enabled;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ObsWebSocketClient> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _socket;
    private string[] _cachedAudioInputNames = Array.Empty<string>();
    private DateTimeOffset _nextAudioTopologyRefresh = DateTimeOffset.MinValue;

    public ObsWebSocketClient(IConfiguration configuration, ILogger<ObsWebSocketClient> log)
    {
        _configuration = configuration;
        _log = log;
        _enabled = configuration.GetValue("TwinA:Obs:Enabled", true);
        _url = configuration["TwinA:Obs:Url"] ?? "ws://127.0.0.1:4455";
    }

    public bool Enabled => _enabled;

    public async Task<ObsStatus> GetStatusAsync(CancellationToken ct)
    {
        if (!_enabled)
            return new(false, false, false, false, 0, "—", false, false, Array.Empty<string>(), Array.Empty<ObsAudioInput>(), "OBS integration is disabled.");

        try
        {
            var record = await RequestAsync("GetRecordStatus", null, ct);
            var replay = await RequestAsync("GetReplayBufferStatus", null, ct);
            var scenesResponse = await RequestAsync("GetSceneList", null, ct);
            var studioResponse = await RequestAsync("GetStudioModeEnabled", null, ct);

            var recording = ReadBool(record, "outputActive");
            var recordingPaused = recording && ReadBool(record, "outputPaused");
            var durationMs = ReadLong(record, "outputDuration");
            var replayActive = ReadBool(replay, "outputActive");
            var scene = ReadString(scenesResponse, "currentProgramSceneName") ?? "—";
            var studioMode = ReadBool(studioResponse, "studioModeEnabled");
            var scenes = ReadNameArray(scenesResponse, "scenes", "sceneName");

            if (DateTimeOffset.UtcNow >= _nextAudioTopologyRefresh)
            {
                _cachedAudioInputNames = await DiscoverAudioInputsAsync(ct);
                _nextAudioTopologyRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
            }

            var audioInputs = new List<ObsAudioInput>(_cachedAudioInputNames.Length);
            foreach (var inputName in _cachedAudioInputNames)
            {
                var audio = await TryGetAudioInputStateAsync(inputName, ct);
                if (audio is not null) audioInputs.Add(audio);
            }

            return new(
                true,
                recording,
                recordingPaused,
                replayActive,
                (int)Math.Max(0, durationMs / 1000),
                scene,
                studioMode,
                true,
                scenes,
                audioInputs.ToArray(),
                null);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OBS status unavailable.");
            return new(false, false, false, false, 0, "—", false, false, Array.Empty<string>(), Array.Empty<ObsAudioInput>(), ex.Message);
        }
    }

    public async Task<ObsCommandOutcome> SetRecordingAsync(bool active, CancellationToken ct)
    {
        var current = await RequestAsync("GetRecordStatus", null, ct);
        var isActive = ReadBool(current, "outputActive");
        if (isActive == active)
            return new(true, true, active ? "OBS recording is already active." : "OBS recording is already stopped.", null);
        return await ToggleRecordingAsync(ct);
    }

    public async Task<ObsCommandOutcome> ToggleRecordingAsync(CancellationToken ct)
    {
        var current = await RequestAsync("GetRecordStatus", null, ct);
        var active = ReadBool(current, "outputActive");

        if (!active)
        {
            await RequestAsync("StartRecord", null, ct);
            var verified = await WaitForAsync(async token => ReadBool(await RequestAsync("GetRecordStatus", null, token), "outputActive"), true, ct);
            return verified
                ? new(true, true, "OBS confirmed recording is active.", null)
                : new(true, false, "OBS accepted StartRecord, but TWIN A could not verify that recording became active.", null);
        }

        var before = DateTimeOffset.UtcNow;
        var response = await RequestAsync("StopRecord", null, ct);
        var outputPath = ReadString(response, "outputPath");
        var stopped = await WaitForAsync(async token => ReadBool(await RequestAsync("GetRecordStatus", null, token), "outputActive"), false, ct);
        var fileVerified = !string.IsNullOrWhiteSpace(outputPath) && await WaitForFileAsync(outputPath!, before, ct);

        if (stopped && fileVerified)
            return new(true, true, $"OBS stopped recording and the file was verified on disk: {outputPath}", outputPath);
        if (stopped)
            return new(true, false, string.IsNullOrWhiteSpace(outputPath)
                ? "OBS confirmed recording stopped, but no output path was returned."
                : $"OBS confirmed recording stopped. The returned path could not yet be verified on disk: {outputPath}", outputPath);

        return new(true, false, "OBS accepted StopRecord, but TWIN A could not verify that recording stopped.", outputPath);
    }

    public async Task<ObsCommandOutcome> ToggleRecordPauseAsync(CancellationToken ct)
    {
        var current = await RequestAsync("GetRecordStatus", null, ct);
        if (!ReadBool(current, "outputActive"))
            return new(false, false, "OBS is not recording, so pause/resume was not performed.", null);

        var paused = ReadBool(current, "outputPaused");
        await RequestAsync(paused ? "ResumeRecord" : "PauseRecord", null, ct);
        var targetPaused = !paused;
        var verified = false;
        for (var i = 0; i < 12; i++)
        {
            var status = await RequestAsync("GetRecordStatus", null, ct);
            if (ReadBool(status, "outputActive") && ReadBool(status, "outputPaused") == targetPaused)
            {
                verified = true;
                break;
            }
            await Task.Delay(150, ct);
        }

        if (!verified)
            return new(true, false, $"OBS accepted the {(targetPaused ? "pause" : "resume")} command, but TWIN A could not verify the resulting pause state.", null);

        return new(true, true, targetPaused ? "OBS confirmed recording is paused." : "OBS confirmed recording has resumed.", null);
    }

    public async Task<ObsCommandOutcome> SetReplayBufferAsync(bool active, CancellationToken ct)
    {
        var current = await RequestAsync("GetReplayBufferStatus", null, ct);
        var isActive = ReadBool(current, "outputActive");
        if (isActive == active)
            return new(true, true, active ? "OBS Replay Buffer is already running." : "OBS Replay Buffer is already stopped.", null);
        return await ToggleReplayBufferAsync(ct);
    }

    public async Task<ObsCommandOutcome> ToggleReplayBufferAsync(CancellationToken ct)
    {
        var current = await RequestAsync("GetReplayBufferStatus", null, ct);
        var active = ReadBool(current, "outputActive");
        await RequestAsync(active ? "StopReplayBuffer" : "StartReplayBuffer", null, ct);
        var verified = await WaitForAsync(async token => ReadBool(await RequestAsync("GetReplayBufferStatus", null, token), "outputActive"), !active, ct);
        return verified
            ? new(true, true, !active ? "OBS confirmed Replay Buffer is running." : "OBS confirmed Replay Buffer is stopped.", null)
            : new(true, false, "OBS accepted the Replay Buffer command, but the resulting state could not be verified.", null);
    }

    public async Task<ObsCommandOutcome> SaveReplayAsync(CancellationToken ct)
    {
        var replay = await RequestAsync("GetReplayBufferStatus", null, ct);
        if (!ReadBool(replay, "outputActive"))
            return new(false, false, "Replay Buffer is not running. Start Replay Buffer before saving a clip.", null);

        var directoryResponse = await RequestAsync("GetRecordDirectory", null, ct);
        var directory = ReadString(directoryResponse, "recordDirectory");
        HashSet<string> before = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            before = Directory.EnumerateFiles(directory).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var started = DateTimeOffset.UtcNow;
        await RequestAsync("SaveReplayBuffer", null, ct);

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            for (var i = 0; i < 20; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = Directory.EnumerateFiles(directory)
                    .Where(path => !before.Contains(path))
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists && file.LastWriteTimeUtc >= started.UtcDateTime.AddSeconds(-1))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                    return new(true, true, $"Replay saved and verified on disk: {candidate.FullName}", candidate.FullName);
                await Task.Delay(250, ct);
            }
        }

        return new(true, false, "OBS confirmed the SaveReplayBuffer request, but TWIN A did not observe a new replay file on disk within 5 seconds.", directory);
    }

    public async Task<ObsCommandOutcome> SetSceneAsync(string scene, CancellationToken ct)
    {
        var scenes = await RequestAsync("GetSceneList", null, ct);
        var existingScenes = ReadNameArray(scenes, "scenes", "sceneName");
        if (!existingScenes.Contains(scene, StringComparer.Ordinal))
            return new(false, false, $"OBS does not currently contain a scene named '{scene}'. No scene change was performed.", scene);

        await RequestAsync("SetCurrentProgramScene", new { sceneName = scene }, ct);
        var verified = await WaitForAsync(async token =>
        {
            var current = await RequestAsync("GetCurrentProgramScene", null, token);
            return string.Equals(ReadString(current, "currentProgramSceneName"), scene, StringComparison.Ordinal);
        }, true, ct);
        return verified
            ? new(true, true, $"OBS confirmed the live program scene is '{scene}'.", scene)
            : new(true, false, $"OBS accepted the scene change to '{scene}', but the resulting program scene could not be verified.", scene);
    }

    public async Task<ObsCommandOutcome> ToggleInputMuteAsync(string inputName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inputName))
            return new(false, false, "No OBS audio input name was supplied.", null);

        var current = await RequestAsync("GetInputMute", new { inputName }, ct);
        var muted = ReadBool(current, "inputMuted");
        var targetMuted = !muted;
        await RequestAsync("SetInputMute", new { inputName, inputMuted = targetMuted }, ct);

        var verified = await WaitForAsync(async token =>
        {
            var status = await RequestAsync("GetInputMute", new { inputName }, token);
            return ReadBool(status, "inputMuted");
        }, targetMuted, ct);

        if (!verified)
            return new(true, false, $"OBS accepted the mute command for '{inputName}', but TWIN A could not verify the resulting state.", inputName);

        return new(true, true, targetMuted
            ? $"OBS confirmed '{inputName}' is muted."
            : $"OBS confirmed '{inputName}' is live.", inputName);
    }

    private async Task<string[]> DiscoverAudioInputsAsync(CancellationToken ct)
    {
        var inputList = await RequestAsync("GetInputList", null, ct);
        var names = ReadNameArray(inputList, "inputs", "inputName");
        var audioNames = new List<string>();

        foreach (var name in names)
        {
            try
            {
                await RequestAsync("GetInputMute", new { inputName = name }, ct);
                await RequestAsync("GetInputVolume", new { inputName = name }, ct);
                audioNames.Add(name);
            }
            catch (InvalidOperationException ex)
            {
                _log.LogTrace(ex, "OBS input {InputName} is not an audio-capable input.", name);
            }
        }

        return audioNames.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task<ObsAudioInput?> TryGetAudioInputStateAsync(string inputName, CancellationToken ct)
    {
        try
        {
            var mute = await RequestAsync("GetInputMute", new { inputName }, ct);
            var volume = await RequestAsync("GetInputVolume", new { inputName }, ct);
            return new ObsAudioInput(inputName, ReadBool(mute, "inputMuted"), ReadDouble(volume, "inputVolumeDb"));
        }
        catch (Exception ex)
        {
            _log.LogTrace(ex, "OBS audio state unavailable for {InputName}.", inputName);
            return null;
        }
    }

    private async Task<JsonElement> RequestAsync(string requestType, object? requestData, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct);
            return await RequestCoreAsync(requestType, requestData, ct);
        }
        catch (WebSocketException)
        {
            ResetSocket();
            throw;
        }
        catch (IOException)
        {
            ResetSocket();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_socket is { State: WebSocketState.Open }) return;
        ResetSocket();

        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("obswebsocket.json");
        await socket.ConnectAsync(new Uri(_url), ct);

        var hello = await ReceiveJsonAsync(socket, ct);
        if (hello.RootElement.GetProperty("op").GetInt32() != 0)
            throw new InvalidOperationException("OBS WebSocket did not send a valid Hello message.");

        var data = hello.RootElement.GetProperty("d");
        var rpcVersion = data.TryGetProperty("rpcVersion", out var rpc) ? rpc.GetInt32() : 1;
        string? authentication = null;

        if (data.TryGetProperty("authentication", out var auth))
        {
            var password = Environment.GetEnvironmentVariable("TWINA_OBS_PASSWORD")
                           ?? _configuration["TwinA:Obs:Password"];
            if (string.IsNullOrEmpty(password))
            {
                socket.Dispose();
                throw new InvalidOperationException("OBS WebSocket authentication is enabled, but TWIN A has no local password. Run scripts\\set-obs-password.ps1, then restart Rider/TWIN A.");
            }

            var salt = auth.GetProperty("salt").GetString() ?? "";
            var challenge = auth.GetProperty("challenge").GetString() ?? "";
            authentication = ComputeAuthentication(password, salt, challenge);
        }

        var identifyData = new Dictionary<string, object?>
        {
            ["rpcVersion"] = rpcVersion,
            ["eventSubscriptions"] = 0
        };
        if (!string.IsNullOrWhiteSpace(authentication)) identifyData["authentication"] = authentication;

        await SendJsonAsync(socket, new { op = 1, d = identifyData }, ct);
        using var identified = await ReceiveJsonAsync(socket, ct);
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
        {
            socket.Dispose();
            throw new InvalidOperationException("OBS WebSocket authentication/identification failed.");
        }

        _socket = socket;
    }

    private async Task<JsonElement> RequestCoreAsync(string requestType, object? requestData, CancellationToken ct)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("OBS WebSocket is not connected.");

        var requestId = Guid.NewGuid().ToString("N");
        await SendJsonAsync(_socket, new
        {
            op = 6,
            d = new
            {
                requestType,
                requestId,
                requestData = requestData ?? new { }
            }
        }, ct);

        while (true)
        {
            using var message = await ReceiveJsonAsync(_socket, ct);
            var root = message.RootElement;
            if (!root.TryGetProperty("op", out var op) || op.GetInt32() != 7) continue;
            var data = root.GetProperty("d");
            if (!string.Equals(data.GetProperty("requestId").GetString(), requestId, StringComparison.Ordinal)) continue;

            var status = data.GetProperty("requestStatus");
            var result = status.GetProperty("result").GetBoolean();
            if (!result)
            {
                var code = status.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : 0;
                var comment = status.TryGetProperty("comment", out var commentEl) ? commentEl.GetString() : null;
                throw new InvalidOperationException($"OBS rejected {requestType} (code {code}){(string.IsNullOrWhiteSpace(comment) ? "." : $": {comment}")}");
            }

            if (data.TryGetProperty("responseData", out var responseData)) return responseData.Clone();
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private static async Task<bool> WaitForAsync(Func<CancellationToken, Task<bool>> probe, bool expected, CancellationToken ct)
    {
        for (var i = 0; i < 12; i++)
        {
            if (await probe(ct) == expected) return true;
            await Task.Delay(150, ct);
        }
        return false;
    }

    private static async Task<bool> WaitForFileAsync(string path, DateTimeOffset started, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Exists && info.LastWriteTimeUtc >= started.UtcDateTime.AddSeconds(-2)) return true;
            }
            await Task.Delay(250, ct);
        }
        return File.Exists(path);
    }

    private static string ComputeAuthentication(string password, string salt, string challenge)
    {
        var secretBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretBytes);
        var authBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authBytes);
    }

    private static bool ReadBool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static long ReadLong(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static double ReadDouble(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : 0;

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[] ReadNameArray(JsonElement element, string arrayProperty, string nameProperty)
    {
        if (!element.TryGetProperty(arrayProperty, out var array) || array.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return array.EnumerateArray()
            .Select(item => ReadString(item, nameProperty))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException($"OBS WebSocket closed the connection: {result.CloseStatus} {result.CloseStatusDescription}");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private void ResetSocket()
    {
        try { _socket?.Dispose(); } catch { }
        _socket = null;
        _cachedAudioInputNames = Array.Empty<string>();
        _nextAudioTopologyRefresh = DateTimeOffset.MinValue;
    }

    public ValueTask DisposeAsync()
    {
        ResetSocket();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record ObsAudioInput(string Name, bool Muted, double VolumeDb);
public sealed record ObsStatus(
    bool Connected,
    bool Recording,
    bool RecordingPaused,
    bool ReplayBuffer,
    int RecordingSeconds,
    string Scene,
    bool StudioMode,
    bool InputsAvailable,
    string[] Scenes,
    ObsAudioInput[] AudioInputs,
    string? Error);
public sealed record ObsCommandOutcome(bool Ok, bool Verified, string Message, string? Data);
