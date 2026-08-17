using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TwinA.DesktopAgent.V07;

internal static class DesktopV07Bootstrap
{
    [ModuleInitializer]
    internal static void Start()
    {
        _ = Task.Run(DesktopV07Agent.RunAsync);
    }
}

internal static class DesktopV07Agent
{
    private const string PipeName = "TwinA.DesktopAgent.V07";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task RunAsync()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new DesktopV07Response(false, "Empty v0.7 command.", null), JsonOptions));
                    continue;
                }

                DesktopV07Response response;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var command = root.TryGetProperty("command", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    var payload = root.TryGetProperty("payload", out var p) ? p : default;
                    response = await DispatchAsync(command, payload);
                }
                catch (Exception ex)
                {
                    response = new DesktopV07Response(false, ex.Message, null);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TWIN A v0.7 desktop channel error: {ex.Message}");
                await Task.Delay(350);
            }
        }
    }

    private static Task<DesktopV07Response> DispatchAsync(string command, JsonElement payload)
    {
        return command switch
        {
            "desktop.runtime.get" => Task.FromResult(Ok("Runtime state read from Windows.", WindowRuntime.GetRuntimeJson())),
            "desktop.windows.get" => Task.FromResult(Ok("Visible top-level windows read from Windows.", WindowRuntime.GetWindowsJson())),
            "desktop.processes.get" => Task.FromResult(Ok("Running processes read from Windows.", WindowRuntime.GetProcessesJson())),
            "desktop.window.action" => Task.FromResult(WindowRuntime.WindowAction(payload)),
            "desktop.process.end" => WindowRuntime.EndProcessAsync(payload),
            "audio.sessions.get" => Task.FromResult(AudioSessionRuntime.GetSessions()),
            "audio.session.set" => Task.FromResult(AudioSessionRuntime.SetSession(payload)),
            "desktop.frame.get" => Task.FromResult(RemoteDesktopRuntime.Capture(payload)),
            "desktop.input" => Task.FromResult(RemoteDesktopRuntime.Input(payload)),
            _ => Task.FromResult(new DesktopV07Response(false, $"Unknown v0.7 desktop command '{command}'.", null))
        };
    }

    internal static DesktopV07Response Ok(string message, string? data = null) => new(true, message, data);
    internal static DesktopV07Response Fail(string message, string? data = null) => new(false, message, data);

    internal static int Int(JsonElement payload, string name, int fallback = 0)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    internal static long Long(JsonElement payload, string name, long fallback = 0)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : fallback;
    }

    internal static double Double(JsonElement payload, string name, double fallback = 0)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out number) ? number : fallback;
    }

    internal static bool? BoolNullable(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    internal static string? String(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

internal sealed record DesktopV07Response(bool Ok, string Message, string? Data);
