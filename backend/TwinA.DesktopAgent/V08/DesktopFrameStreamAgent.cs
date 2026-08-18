using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TwinA.DesktopAgent.V08;

internal static class DesktopFrameStreamBootstrap
{
    [ModuleInitializer]
    internal static void Start()
    {
        _ = Task.Run(DesktopFrameStreamAgent.RunAsync);
    }
}

internal static class DesktopFrameStreamAgent
{
    private const string PipeName = "TwinA.DesktopAgent.FrameV08";
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
                var configLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(configLine)) continue;

                var config = JsonSerializer.Deserialize<DesktopFrameStreamConfig>(configLine, JsonOptions) ?? new DesktopFrameStreamConfig();
                config.MaxWidth = Math.Clamp(config.MaxWidth, 640, 4096);
                config.Quality = Math.Clamp(config.Quality, 25, 92);
                config.Fps = Math.Clamp(config.Fps, 1, 60);

                using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                var targetFrameMs = 1000d / config.Fps;

                while (pipe.IsConnected)
                {
                    var started = Stopwatch.GetTimestamp();
                    CapturedDesktopFrame frame;
                    try
                    {
                        frame = MonitorRuntime.CaptureJpeg(config.MonitorId, config.MaxWidth, config.Quality);
                    }
                    catch
                    {
                        break;
                    }

                    writer.Write(frame.CapturedAtUnixMs);
                    writer.Write(frame.Width);
                    writer.Write(frame.Height);
                    writer.Write(frame.Bytes.Length);
                    writer.Write(frame.Bytes);
                    writer.Flush();

                    var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var remaining = targetFrameMs - elapsedMs;
                    if (remaining > 1)
                        await Task.Delay(TimeSpan.FromMilliseconds(remaining));
                }
            }
            catch (IOException)
            {
                // Client disconnected. Accept the next remote viewer.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TWIN A v0.8 frame stream error: {ex.Message}");
                await Task.Delay(250);
            }
        }
    }
}

internal sealed class DesktopFrameStreamConfig
{
    public string MonitorId { get; set; } = "all";
    public int MaxWidth { get; set; } = 1600;
    public int Quality { get; set; } = 58;
    public int Fps { get; set; } = 60;
}
