using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TwinA.ControlServer.Services;

public sealed class DesktopFrameStreamClient
{
    private const string PipeName = "TwinA.DesktopAgent.FrameV08";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RelayAsync(WebSocket socket, DesktopStreamOptions options, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1200, ct);

        var config = JsonSerializer.Serialize(new
        {
            monitorId = string.IsNullOrWhiteSpace(options.MonitorId) ? "all" : options.MonitorId,
            maxWidth = Math.Clamp(options.MaxWidth, 640, 4096),
            quality = Math.Clamp(options.Quality, 25, 92),
            fps = Math.Clamp(options.Fps, 1, 60)
        }, JsonOptions) + "\n";
        var configBytes = Encoding.UTF8.GetBytes(config);
        await pipe.WriteAsync(configBytes, ct);
        await pipe.FlushAsync(ct);

        var header = new byte[20];
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await pipe.ReadExactlyAsync(header, ct);
            var capturedAt = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0, 8));
            var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
            var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16, 4));
            if (length <= 0 || length > 16 * 1024 * 1024)
                throw new InvalidDataException($"Desktop Agent returned an invalid frame size: {length} bytes.");

            var jpeg = GC.AllocateUninitializedArray<byte>(length);
            await pipe.ReadExactlyAsync(jpeg, ct);

            var packet = GC.AllocateUninitializedArray<byte>(16 + length);
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(0, 8), capturedAt);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12, 4), height);
            Buffer.BlockCopy(jpeg, 0, packet, 16, length);

            await socket.SendAsync(packet, WebSocketMessageType.Binary, true, ct);
        }
    }
}

public sealed record DesktopStreamOptions(string MonitorId, int MaxWidth, int Quality, int Fps);
