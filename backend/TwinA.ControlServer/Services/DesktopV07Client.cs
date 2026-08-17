using System.IO.Pipes;
using System.Text.Json;

namespace TwinA.ControlServer.Services;

public sealed class DesktopV07Client
{
    private const string PipeName = "TwinA.DesktopAgent.V07";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(bool Ok, string Message, string? Data)> SendAsync(string command, Dictionary<string, object?>? payload, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(900, ct);
        }
        catch
        {
            return (false, "TWIN A Desktop Agent v0.7 control channel is not available in the interactive Windows session.", null);
        }

        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command, payload = payload ?? new() }, JsonOptions));
        var responseLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(responseLine)) return (false, "Desktop Agent v0.7 returned no response.", null);

        var response = JsonSerializer.Deserialize<DesktopResponse>(responseLine, JsonOptions);
        return response is null
            ? (false, "Invalid Desktop Agent v0.7 response.", null)
            : (response.Ok, response.Message, response.Data);
    }

    private sealed record DesktopResponse(bool Ok, string Message, string? Data);
}
