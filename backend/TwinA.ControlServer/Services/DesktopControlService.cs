using System.Text.Json;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class DesktopControlService
{
    private readonly DesktopV07Client _agent;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DesktopControlService(DesktopV07Client agent)
    {
        _agent = agent;
    }

    public Task<(bool Ok, string Message, string? Data)> RuntimeAsync(CancellationToken ct)
        => _agent.SendAsync("desktop.runtime.get", new(), ct);

    public Task<(bool Ok, string Message, string? Data)> WindowsAsync(CancellationToken ct)
        => _agent.SendAsync("desktop.windows.get", new(), ct);

    public Task<(bool Ok, string Message, string? Data)> ProcessesAsync(CancellationToken ct)
        => _agent.SendAsync("desktop.processes.get", new(), ct);

    public Task<(bool Ok, string Message, string? Data)> AudioSessionsAsync(CancellationToken ct)
        => _agent.SendAsync("audio.sessions.get", new(), ct);

    public async Task<CommandResult> WindowActionAsync(long handle, string action, CancellationToken ct)
    {
        if (handle == 0) return CommandResult.Failure("desktop.window.action", "A valid window handle is required.");
        if (string.IsNullOrWhiteSpace(action)) return CommandResult.Failure("desktop.window.action", "A window action is required.");

        var result = await _agent.SendAsync("desktop.window.action", new()
        {
            ["handle"] = handle,
            ["action"] = action
        }, ct);

        return result.Ok
            ? CommandResult.Success("desktop.window.action", result.Message, result.Data)
            : CommandResult.Failure("desktop.window.action", result.Message, result.Data);
    }

    public async Task<CommandResult> EndProcessAsync(int pid, CancellationToken ct)
    {
        if (pid <= 0) return CommandResult.Failure("desktop.process.end", "A valid process id is required.");
        if (pid == Environment.ProcessId)
            return CommandResult.Failure("desktop.process.end", "TWIN A Control Server protects itself from termination.");

        var result = await _agent.SendAsync("desktop.process.end", new()
        {
            ["pid"] = pid
        }, ct);

        return result.Ok
            ? CommandResult.Success("desktop.process.end", result.Message, result.Data)
            : CommandResult.Failure("desktop.process.end", result.Message, result.Data);
    }

    public async Task<CommandResult> SetAudioSessionAsync(int pid, double? volume, bool? muted, CancellationToken ct)
    {
        if (pid <= 0) return CommandResult.Failure("audio.session.set", "A valid process id is required.");
        if (volume is null && muted is null)
            return CommandResult.Failure("audio.session.set", "No audio-session change was supplied.");

        var payload = new Dictionary<string, object?> { ["pid"] = pid };
        if (volume is not null) payload["volume"] = Math.Clamp(volume.Value, 0, 100);
        if (muted is not null) payload["muted"] = muted.Value;

        var result = await _agent.SendAsync("audio.session.set", payload, ct);
        return result.Ok
            ? CommandResult.Success("audio.session.set", result.Message, result.Data)
            : CommandResult.Failure("audio.session.set", result.Message, result.Data);
    }

    public async Task<(bool Ok, string Message, byte[]? Bytes)> CaptureFrameAsync(int maxWidth, int quality, CancellationToken ct)
    {
        var result = await _agent.SendAsync("desktop.frame.get", new()
        {
            ["maxWidth"] = Math.Clamp(maxWidth, 640, 2560),
            ["quality"] = Math.Clamp(quality, 30, 90)
        }, ct);

        if (!result.Ok || string.IsNullOrWhiteSpace(result.Data))
            return (false, result.Message, null);

        try
        {
            using var document = JsonDocument.Parse(result.Data);
            var base64 = document.RootElement.GetProperty("base64").GetString();
            if (string.IsNullOrWhiteSpace(base64))
                return (false, "Desktop Agent returned an empty desktop frame.", null);
            return (true, result.Message, Convert.FromBase64String(base64));
        }
        catch (Exception ex)
        {
            return (false, $"Desktop frame could not be decoded: {ex.Message}", null);
        }
    }

    public async Task<CommandResult> InputAsync(DesktopInputRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Action))
            return CommandResult.Failure("desktop.input", "A remote input action is required.");

        var result = await _agent.SendAsync("desktop.input", new()
        {
            ["action"] = request.Action,
            ["x"] = Math.Clamp(request.X, 0, 1),
            ["y"] = Math.Clamp(request.Y, 0, 1),
            ["delta"] = request.Delta,
            ["key"] = request.Key,
            ["text"] = request.Text
        }, ct);

        return result.Ok
            ? CommandResult.Executed("desktop.input", result.Message, result.Data)
            : CommandResult.Failure("desktop.input", result.Message, result.Data);
    }

    public static IResult JsonData((bool Ok, string Message, string? Data) result)
    {
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Data))
            return Results.Problem(result.Message, statusCode: 503);
        return Results.Text(result.Data, "application/json");
    }
}

public sealed record DesktopWindowActionRequest(long Handle, string Action);
public sealed record DesktopProcessEndRequest(int Pid);
public sealed record DesktopAudioSessionRequest(int Pid, double? Volume, bool? Muted);
public sealed record DesktopInputRequest(string Action, double X = 0.5, double Y = 0.5, int Delta = 0, string? Key = null, string? Text = null);
