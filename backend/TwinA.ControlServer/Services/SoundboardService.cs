using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class SoundboardService
{
    private static readonly string[] AllowedExtensions = [".wav", ".mp3", ".m4a", ".aac", ".wma"];
    public string Root { get; }
    public SoundboardService()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinAControlCenter", "Sounds");
        Directory.CreateDirectory(Root);
    }

    public SoundInfo[] List() => Directory.EnumerateFiles(Root)
        .Where(f => AllowedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
        .Select(f => new FileInfo(f))
        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
        .Select(f => new SoundInfo(ToId(f.Name), Path.GetFileNameWithoutExtension(f.Name), f.Name, f.Extension, f.Length, f.LastWriteTimeUtc))
        .ToArray();

    public async Task<string> SaveAsync(string fileName, Stream stream, CancellationToken ct)
    {
        var safe = Path.GetFileName(fileName);
        var ext = Path.GetExtension(safe);
        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Supported sound formats: WAV, MP3, M4A, AAC, WMA.");
        var target = Unique(Path.Combine(Root, safe));
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            await stream.CopyToAsync(output, ct);
        if (!File.Exists(target) || new FileInfo(target).Length == 0)
        {
            try { File.Delete(target); } catch { }
            throw new IOException("Sound upload completed without a verifiable non-empty file.");
        }
        return target;
    }

    public string GetPath(string id)
    {
        var file = List().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Sound not found.");
        return Path.Combine(Root, file.FileName);
    }

    public bool Delete(string id)
    {
        var path = GetPath(id); File.Delete(path); return !File.Exists(path);
    }

    private static string ToId(string name) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..16];
    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir=Path.GetDirectoryName(path)!; var stem=Path.GetFileNameWithoutExtension(path); var ext=Path.GetExtension(path);
        for(var i=2;i<9999;i++){var c=Path.Combine(dir,$"{stem} ({i}){ext}");if(!File.Exists(c))return c;} throw new IOException("Could not choose a unique sound filename.");
    }
}
