using System.IO.Pipes;
using System.Text.Json;

namespace TwinA.ControlServer.Services;

public sealed class DesktopAgentClient
{
    private const string PipeName = "TwinA.DesktopAgent";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(bool Ok, string Message, string? Data)> SendAsync(string command, Dictionary<string, object?>? payload, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(700, ct);
        }
        catch
        {
            return (false, "Desktop Agent is not running in the interactive Windows session.", null);
        }

        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command, payload = payload ?? new() }, JsonOptions));
        var responseLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(responseLine)) return (false, "Desktop Agent returned no response.", null);
        var response = JsonSerializer.Deserialize<AgentResponse>(responseLine, JsonOptions);
        return response is null ? (false, "Invalid Desktop Agent response.", null) : (response.Ok, response.Message, response.Data);
    }

    private sealed record AgentResponse(bool Ok, string Message, string? Data);
}
