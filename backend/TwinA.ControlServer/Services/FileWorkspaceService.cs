using System.Diagnostics;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class FileWorkspaceService
{
    private readonly SettingsStore _settings;
    public FileWorkspaceService(SettingsStore settings) => _settings = settings;

    public DriveInfoDto[] GetDrives()
        => DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => new DriveInfoDto(d.Name, d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace))
            .OrderBy(d => d.Name)
            .ToArray();

    public FileEntryDto[] Browse(string path)
    {
        var full = ValidateExistingDirectory(path);
        var entries = new List<FileEntryDto>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
        foreach (var dir in Directory.EnumerateDirectories(full, "*", options))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                entries.Add(new FileEntryDto(info.Name, info.FullName, true, 0, info.LastWriteTimeUtc, "", IsProtected(info.FullName)));
            }
            catch { }
        }
        foreach (var file in Directory.EnumerateFiles(full, "*", options))
        {
            try
            {
                var info = new FileInfo(file);
                entries.Add(new FileEntryDto(info.Name, info.FullName, false, info.Length, info.LastWriteTimeUtc, info.Extension, IsProtected(info.FullName)));
            }
            catch { }
        }
        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string OpenOnPc(string path)
    {
        var full = ValidateExistingPath(path);
        _ = Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true })
            ?? throw new InvalidOperationException("Windows did not open the selected item.");
        return full;
    }

    public string CreateFolder(string parent, string name)
    {
        var root = ValidateExistingDirectory(parent);
        ValidateSimpleName(name);
        EnsureMutationAllowed(root, allowDriveRootContainer: true);
        var target = Path.Combine(root, name);
        Directory.CreateDirectory(target);
        if (!Directory.Exists(target)) throw new IOException("The folder was not created.");
        return target;
    }

    public string Rename(string path, string newName)
    {
        var full = ValidateExistingPath(path);
        ValidateSimpleName(newName);
        EnsureMutationAllowed(full);
        var parent = Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Cannot rename a drive root.");
        var target = Path.Combine(parent, newName);
        if (Directory.Exists(full)) Directory.Move(full, target);
        else File.Move(full, target);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new IOException("The renamed item could not be verified.");
        return target;
    }

    public string Move(string source, string destinationDirectory)
    {
        var full = ValidateExistingPath(source);
        var destination = ValidateExistingDirectory(destinationDirectory);
        EnsureMutationAllowed(full);
        EnsureMutationAllowed(destination, allowDriveRootContainer: true);
        var target = Path.Combine(destination, Path.GetFileName(full));
        if (Directory.Exists(full)) Directory.Move(full, target);
        else File.Move(full, target, false);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new IOException("Move completed without a verifiable destination.");
        return target;
    }

    public string Copy(string source, string destinationDirectory)
    {
        var full = ValidateExistingPath(source);
        var destination = ValidateExistingDirectory(destinationDirectory);
        EnsureMutationAllowed(destination, allowDriveRootContainer: true);
        var target = Path.Combine(destination, Path.GetFileName(full));
        if (Directory.Exists(full)) CopyDirectory(full, target);
        else File.Copy(full, target, false);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new IOException("Copy completed without a verifiable destination.");
        return target;
    }

    public void Delete(string path)
    {
        var full = ValidateExistingPath(path);
        EnsureMutationAllowed(full);
        if (Directory.Exists(full)) Directory.Delete(full, true);
        else File.Delete(full);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Delete command completed but the item still exists.");
    }

    public async Task<string> SaveUploadAsync(string directory, string fileName, Stream source, CancellationToken ct)
    {
        var dir = ValidateExistingDirectory(directory);
        EnsureMutationAllowed(dir, allowDriveRootContainer: true);
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) throw new InvalidOperationException("Upload has no valid filename.");
        var target = UniquePath(Path.Combine(dir, safeName));
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            await source.CopyToAsync(output, ct);
        if (!File.Exists(target)) throw new IOException("Upload completed but the file was not found.");
        return target;
    }

    public string ValidateDownload(string path)
    {
        var full = ValidateExistingPath(path);
        if (!File.Exists(full)) throw new InvalidOperationException("Only files can be downloaded.");
        return full;
    }

    public bool IsProtected(string path)
    {
        if (!_settings.Get().Ui.ProtectSystemPaths) return false;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(Path.DirectorySeparatorChar);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd(Path.DirectorySeparatorChar);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd(Path.DirectorySeparatorChar);
        return IsUnder(full, windows) || IsUnder(full, programFiles) || IsUnder(full, programFilesX86) || IsUnder(full, system) || Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar).Equals(full, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void EnsureMutationAllowed(string path, bool allowDriveRootContainer = false)
    {
        if (allowDriveRootContainer && IsDriveRoot(path)) return;
        if (IsProtected(path))
            throw new InvalidOperationException("This path is protected by TWIN A. Browsing/downloading is allowed, but destructive file operations are blocked here. You can change this guard rail in Settings.");
    }

    private static bool IsDriveRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(root) && full.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private string ValidateExistingDirectory(string path)
    {
        var full = NormalizeAndValidateDrive(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Folder not found: {full}");
        return full;
    }

    private string ValidateExistingPath(string path)
    {
        var full = NormalizeAndValidateDrive(path);
        if (!Directory.Exists(full) && !File.Exists(full)) throw new FileNotFoundException($"Path not found: {full}");
        return full;
    }

    private string NormalizeAndValidateDrive(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("No path was supplied.");
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("The path is not on a local drive.");
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (drive is null || !drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new InvalidOperationException("Only ready local fixed/removable drives are accessible.");
        return full;
    }

    private static void ValidateSimpleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Invalid file or folder name.");
    }

    private static bool IsUnder(string full, string root)
        => full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not choose a unique destination filename.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
