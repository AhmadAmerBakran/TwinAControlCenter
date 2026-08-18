using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class SteamLibraryService
{
    private readonly SettingsStore _settings;
    private readonly ProcessRunner _process;
    private readonly ILogger<SteamLibraryService> _log;

    public SteamLibraryService(SettingsStore settings, ProcessRunner process, ILogger<SteamLibraryService> log)
    { _settings = settings; _process = process; _log = log; }

    public IReadOnlyList<SteamGameInfo> GetGames()
    {
        var result = new List<SteamGameInfo>();
        foreach (var library in GetLibraryPaths())
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    if (!uint.TryParse(ReadVdfValue(text, "appid"), out var appId)) continue;
                    var name = ReadVdfValue(text, "name") ?? $"Steam {appId}";
                    var installDirName = ReadVdfValue(text, "installdir") ?? "";
                    var installDirectory = Path.Combine(steamApps, "common", installDirName);
                    var running = FindRunningProcessUnder(installDirectory);
                    result.Add(new SteamGameInfo(
                        $"steam-{appId}", appId, name, installDirectory, library,
                        $"steam://rungameid/{appId}", Directory.Exists(installDirectory), running is not null,
                        running?.ProcessName));
                }
                catch (Exception ex) { _log.LogDebug(ex, "Could not read Steam manifest {Manifest}", manifest); }
            }
        }
        return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<object> GetAllGames()
    {
        var steam = GetGames().Cast<object>().ToList();
        foreach (var game in _settings.Get().CustomGames)
        {
            var processName = string.IsNullOrWhiteSpace(game.ProcessName) ? null : Path.GetFileNameWithoutExtension(game.ProcessName);
            var running = processName is not null && Process.GetProcessesByName(processName).Length > 0;
            var expandedTarget = Environment.ExpandEnvironmentVariables(game.LaunchTarget ?? "");
            var installed = !string.IsNullOrWhiteSpace(expandedTarget) &&
                            (expandedTarget.Contains("://", StringComparison.Ordinal) || File.Exists(expandedTarget));
            steam.Add(new
            {
                id = game.Id, appId = 0u, name = game.Name, installDirectory = game.WorkingDirectory ?? "", libraryPath = "Custom",
                launchUri = game.LaunchTarget, installed, running,
                runningProcess = processName, custom = true, config = game
            });
        }
        return steam;
    }

    public async Task<(bool Ok, bool Verified, string Message, string? Data)> OpenLibraryAsync(CancellationToken ct)
    {
        _process.Open("steam://open/games");
        for (var i = 0; i < 20; i++)
        {
            if (Process.GetProcessesByName("steam").Length > 0)
                return (true, false, "Steam is running and the Library URI was sent. Steam does not expose the currently displayed client page for independent verification.", "steam://open/games");
            await Task.Delay(250, ct);
        }
        return (false, false, "Steam Library was requested but the Steam client could not be verified as running.", null);
    }

    public async Task<(bool Ok, bool Verified, string Message, string? Data)> LaunchAsync(string id, CancellationToken ct)
    {
        var steamGame = GetGames().FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (steamGame is not null)
        {
            var before = Process.GetProcesses().Select(p => p.Id).ToHashSet();
            _process.Open(steamGame.LaunchUri);
            for (var i = 0; i < 60; i++)
            {
                ct.ThrowIfCancellationRequested();
                var running = FindRunningProcessUnder(steamGame.InstallDirectory);
                if (running is not null)
                    return (true, true, $"{steamGame.Name} is running. TWIN A verified process '{running.ProcessName}'.", running.ProcessName);
                await Task.Delay(250, ct);
            }
            return (true, false, $"Steam accepted the launch request for {steamGame.Name}, but TWIN A could not verify a process inside its install folder within 15 seconds.", steamGame.LaunchUri);
        }

        var custom = _settings.Get().CustomGames.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (custom is null) return (false, false, "The selected game no longer exists in the configured library.", null);
        if (string.IsNullOrWhiteSpace(custom.LaunchTarget)) return (false, false, $"{custom.Name} has no launch target.", null);

        _process.Open(Environment.ExpandEnvironmentVariables(custom.LaunchTarget), string.IsNullOrWhiteSpace(custom.Arguments) ? null : custom.Arguments, string.IsNullOrWhiteSpace(custom.WorkingDirectory) ? null : Environment.ExpandEnvironmentVariables(custom.WorkingDirectory));
        if (!string.IsNullOrWhiteSpace(custom.ProcessName))
        {
            var processName = Path.GetFileNameWithoutExtension(custom.ProcessName);
            for (var i = 0; i < 60; i++)
            {
                if (Process.GetProcessesByName(processName).Length > 0)
                    return (true, true, $"{custom.Name} is running and process '{processName}' was verified.", processName);
                await Task.Delay(250, ct);
            }
            return (true, false, $"The launch request for {custom.Name} was executed, but process '{processName}' was not observed within 15 seconds.", null);
        }
        return (true, false, $"The launch request for {custom.Name} was executed. Add a process name in Settings to enable launch verification.", null);
    }

    public string SteamPath => ReadSteamPath() ?? @"C:\Program Files (x86)\Steam";

    private IEnumerable<string> GetLibraryPaths()
    {
        var root = SteamPath.Replace('/', Path.DirectorySeparatorChar);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return paths;
        var text = File.ReadAllText(vdf);
        foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups["path"].Value.Replace("\\\\", "\\").Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(path)) paths.Add(path);
        }
        return paths;
    }

    private static string? ReadSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath")?.ToString();
        }
        catch { return null; }
    }

    private static string? ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static Process? FindRunningProcessUnder(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory)) return null;
        var prefix = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return process;
            }
            catch { }
        }
        return null;
    }
}
