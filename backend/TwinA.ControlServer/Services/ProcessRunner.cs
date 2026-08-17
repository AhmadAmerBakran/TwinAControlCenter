using System.Diagnostics;

namespace TwinA.ControlServer.Services;

public sealed class ProcessRunner
{
    public void Open(string target, string? arguments = null, string? workingDirectory = null)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException($"Windows did not accept the launch request for {target}.");
    }

    public async Task<(int ExitCode,string Output,string Error)> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdout, await stderr);
    }

    public Task<(int ExitCode,string Output,string Error)> PowerShellAsync(string script, CancellationToken ct)
        => RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"", ct);
}
