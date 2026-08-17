using System.Text.Json;

namespace TwinA.ControlServer.Services;

public sealed class SessionMarkerService
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SessionMarkerService(IConfiguration config)
    {
        _root = Environment.ExpandEnvironmentVariables(config["TwinA:DataRoot"] ?? "%USERPROFILE%\\Videos\\TwinAControl");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<string> AddAsync(string kind, string? note, int seconds, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var file = Path.Combine(_root, $"markers-{DateTime.Now:yyyy-MM-dd}.jsonl");
            var beforeLength = File.Exists(file) ? new FileInfo(file).Length : 0L;
            var entry = new { atSeconds = seconds, kind, note = note ?? "", createdAt = DateTimeOffset.Now };
            await File.AppendAllTextAsync(file, JsonSerializer.Serialize(entry) + Environment.NewLine, ct);
            var info = new FileInfo(file);
            info.Refresh();
            if (!info.Exists || info.Length <= beforeLength)
                throw new IOException("The marker write completed without a verifiable file change.");
            return file;
        }
        finally { _gate.Release(); }
    }
}
