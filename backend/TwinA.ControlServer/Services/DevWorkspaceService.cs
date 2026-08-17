using System.Diagnostics;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class DevWorkspaceService
{
    private readonly SettingsStore _settings;
    private readonly ProcessRunner _process;
    public DevWorkspaceService(SettingsStore settings, ProcessRunner process) { _settings = settings; _process = process; }

    public IReadOnlyList<DevProjectConfig> GetProjects() => _settings.Get().DevProjects;

    public DevProjectConfig Upsert(DevProjectConfig project)
    {
        project.Id = string.IsNullOrWhiteSpace(project.Id) ? Guid.NewGuid().ToString("N") : project.Id;
        project.Name = string.IsNullOrWhiteSpace(project.Name) ? "Project" : project.Name.Trim();
        project.WorkingDirectory = Environment.ExpandEnvironmentVariables(project.WorkingDirectory ?? "");
        return _settings.Update(settings =>
        {
            var index = settings.DevProjects.FindIndex(p => p.Id.Equals(project.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) settings.DevProjects[index] = project; else settings.DevProjects.Add(project);
            return project;
        });
    }

    public bool Delete(string id) => !id.Equals("twina", StringComparison.OrdinalIgnoreCase) && _settings.Update(settings => settings.DevProjects.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0);

    public async Task<DevProjectStatus> GetStatusAsync(string id, CancellationToken ct)
    {
        var p = Find(id);
        var exists = Directory.Exists(p.WorkingDirectory);
        var branch = "—";
        var changes = 0;
        var summary = "Not a Git repository";
        if (exists)
        {
            var inside = await RunShellAsync("git rev-parse --is-inside-work-tree", p.WorkingDirectory, ct, 10);
            if (inside.ExitCode == 0 && inside.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                var branchResult = await RunShellAsync("git rev-parse --abbrev-ref HEAD", p.WorkingDirectory, ct, 10);
                if (branchResult.ExitCode == 0) branch = branchResult.Output.Trim();
                var status = await RunShellAsync("git status --porcelain", p.WorkingDirectory, ct, 10);
                if (status.ExitCode == 0)
                {
                    changes = status.Output.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).Length;
                    summary = changes == 0 ? "Clean" : $"{changes} changed file(s)";
                }
            }
        }

        return new DevProjectStatus(
            p.Id, p.Name, p.WorkingDirectory, exists, branch, changes, summary,
            await ToolVersionAsync("dotnet", "--version", ct),
            await ToolVersionAsync("node", "--version", ct),
            await ToolVersionAsync("git", "--version", ct),
            await ToolVersionAsync("docker", "--version", ct));
    }

    public async Task<(bool Ok, bool Verified, string Message, string? Data)> ExecuteAsync(string id, string action, CancellationToken ct)
    {
        var p = Find(id);
        if (!Directory.Exists(p.WorkingDirectory)) return (false, false, $"Project folder does not exist: {p.WorkingDirectory}", null);
        switch (action.ToLowerInvariant())
        {
            case "build": return await ExecuteCommandAsync(p, p.BuildCommand, "Build", ct);
            case "test": return await ExecuteCommandAsync(p, p.TestCommand, "Tests", ct);
            case "run":
            {
                if (p.Id.Equals("twina", StringComparison.OrdinalIgnoreCase))
                    return (true, true, "TWIN A Control Server is already running — this request reached it successfully. No duplicate server was started.", null);
                if (string.IsNullOrWhiteSpace(p.RunCommand)) return (false, false, "No run command is configured for this project.", null);
                Process.Start(new ProcessStartInfo("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{p.WorkingDirectory.Replace("'","''")}'; {p.RunCommand}\"") { UseShellExecute = true });
                return (true, false, "Run command was started in a new terminal. Its long-running final state is not automatically verifiable.", p.RunCommand);
            }
            case "rider":
            {
                var ide = string.IsNullOrWhiteSpace(p.IdePath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Rider", "bin", "rider64.exe") : Environment.ExpandEnvironmentVariables(p.IdePath);
                if (!File.Exists(ide)) return (false, false, $"Rider executable not found: {ide}", null);
                var target = !string.IsNullOrWhiteSpace(p.SolutionOrProject) && File.Exists(p.SolutionOrProject) ? p.SolutionOrProject : p.WorkingDirectory;
                _process.Open(ide, $"\"{target}\"");
                for (var i=0;i<20;i++) { if (Process.GetProcessesByName("rider64").Length>0) return (true,false,"Rider is running and the project-open request was sent, but the exact opened project tab/window is not independently observable.",target); await Task.Delay(250,ct); }
                return (false,false,"Rider launch was requested but the Rider process was not verified.",target);
            }
            case "folder": _process.Open(p.WorkingDirectory); return (true, false, $"Windows accepted the Explorer request for the verified project folder. Explorer window state is not independently observable.", p.WorkingDirectory);
            default: return (false, false, $"Unknown developer action '{action}'.", null);
        }
    }

    private async Task<(bool Ok, bool Verified, string Message, string? Data)> ExecuteCommandAsync(DevProjectConfig p, string command, string label, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return (false, false, $"No {label.ToLowerInvariant()} command is configured for this project.", null);
        var result = await RunShellAsync(command, p.WorkingDirectory, ct, 300);
        var output = (result.Output + (string.IsNullOrWhiteSpace(result.Error) ? "" : "\n" + result.Error)).Trim();
        if (output.Length > 12000) output = output[^12000..];
        return result.ExitCode == 0
            ? (true, true, $"{label} completed successfully with exit code 0.", output)
            : (false, false, $"{label} failed with exit code {result.ExitCode}.", output);
    }

    private DevProjectConfig Find(string id) => _settings.Get().DevProjects.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Developer project not found.");

    private static async Task<(int ExitCode, string Output, string Error)> RunShellAsync(string command, string workingDirectory, CancellationToken ct, int timeoutSeconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe", WorkingDirectory = workingDirectory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d"); startInfo.ArgumentList.Add("/s"); startInfo.ArgumentList.Add("/c"); startInfo.ArgumentList.Add(command);
        using var p = new Process { StartInfo = startInfo };
        if (!p.Start()) throw new InvalidOperationException("Could not start developer command.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch {} throw new TimeoutException($"Command exceeded {timeoutSeconds} seconds."); }
        return (p.ExitCode, await stdout, await stderr);
    }

    private static async Task<string> ToolVersionAsync(string file, string args, CancellationToken ct)
    {
        try
        {
            using var p = new Process { StartInfo = new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true} };
            if (!p.Start()) return "NOT INSTALLED";
            var output = await p.StandardOutput.ReadToEndAsync(); var error = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(ct);
            var text = string.IsNullOrWhiteSpace(output) ? error : output;
            return p.ExitCode == 0 ? text.Trim().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "AVAILABLE" : "NOT INSTALLED";
        }
        catch { return "NOT INSTALLED"; }
    }
}
