using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TwinA.ControlServer.Hubs;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class SystemTelemetryService : BackgroundService
{
    private readonly ControlState _state;
    private readonly DesktopAgentClient _agent;
    private readonly ProcessRunner _process;
    private readonly ObsWebSocketClient _obs;
    private readonly SystemInfoService _systemInfo;
    private readonly IHubContext<StateHub> _hub;
    private readonly ILogger<SystemTelemetryService> _log;
    private DateTimeOffset _nextTailscaleCheck = DateTimeOffset.MinValue;

    public SystemTelemetryService(ControlState state, DesktopAgentClient agent, ProcessRunner process, ObsWebSocketClient obs, SystemInfoService systemInfo, IHubContext<StateHub> hub, ILogger<SystemTelemetryService> log)
    { _state = state; _agent = agent; _process = process; _obs = obs; _systemInfo = systemInfo; _hub = hub; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDesktopAgent(stoppingToken);
                await RefreshObs(stoppingToken);
                RefreshNetwork();
                if (DateTimeOffset.UtcNow >= _nextTailscaleCheck)
                {
                    await RefreshTailscale(stoppingToken);
                    _nextTailscaleCheck = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                await _hub.Clients.All.SendAsync("snapshot", _state.Snapshot, stoppingToken);
            }
            catch (Exception ex) { _log.LogDebug(ex, "Telemetry broadcast skipped."); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task RefreshDesktopAgent(CancellationToken ct)
    {
        var result = await _agent.SendAsync("system.state.get", new(), ct);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Data))
        {
            _state.Update(s => s with { Agent = "offline" });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Data);
            var root = doc.RootElement;
            var volume = ReadInt(root, "volume", _state.Snapshot.MasterVolume);
            var muted = root.TryGetProperty("muted", out var m) && m.GetBoolean();
            var cpu = ReadInt(root, "cpu", _state.Snapshot.Cpu);
            var gpu = ReadInt(root, "gpu", _state.Snapshot.Gpu);
            var ram = ReadInt(root, "ram", _state.Snapshot.Ram);
            var gpuTemp = ReadInt(root, "gpuTemp", _state.Snapshot.GpuTemp);
            _state.Update(s => s with
            {
                Agent = "online",
                MasterVolume = Math.Clamp(volume, 0, 100),
                MasterMuted = muted,
                Cpu = Math.Clamp(cpu, 0, 100),
                Gpu = Math.Clamp(gpu, 0, 100),
                Ram = Math.Clamp(ram, 0, 100),
                GpuTemp = Math.Clamp(gpuTemp, 0, 120)
            });
        }
        catch
        {
            _state.Update(s => s with { Agent = "online" });
        }
    }

    private async Task RefreshObs(CancellationToken ct)
    {
        var status = await _obs.GetStatusAsync(ct);
        if (!status.Connected)
        {
            _state.Update(s => s with
            {
                Obs = status.Error?.Contains("password", StringComparison.OrdinalIgnoreCase) == true ? "warning" : "offline",
                RecordingPaused = false,
                StudioMode = false,
                Scenes = Array.Empty<string>(),
                ObsAudioSources = Array.Empty<ObsAudioSourceState>()
            });
            _state.SetRecording(false, 0, false);
            return;
        }

        var audioSources = status.AudioInputs
            .Select(input => new ObsAudioSourceState(input.Name, input.Muted, Math.Round(input.VolumeDb, 1)))
            .ToArray();
        var mic = audioSources.FirstOrDefault(source => source.Name.Equals("Mic/Aux", StringComparison.OrdinalIgnoreCase))
                  ?? audioSources.FirstOrDefault(source => source.Name.Contains("mic", StringComparison.OrdinalIgnoreCase));
        var desktop = audioSources.FirstOrDefault(source => source.Name.Equals("Desktop Audio", StringComparison.OrdinalIgnoreCase))
                      ?? audioSources.FirstOrDefault(source => source.Name.Contains("desktop", StringComparison.OrdinalIgnoreCase));

        _state.Update(s => s with
        {
            Obs = "ready",
            ReplayBuffer = status.ReplayBuffer,
            RecordingPaused = status.RecordingPaused,
            StudioMode = status.StudioMode,
            Scene = string.IsNullOrWhiteSpace(status.Scene) ? "—" : status.Scene,
            Scenes = status.Scenes,
            ObsAudioSources = audioSources,
            MicMuted = mic?.Muted ?? s.MicMuted,
            DesktopMuted = desktop?.Muted ?? s.DesktopMuted
        });
        _state.SetRecording(status.Recording, status.RecordingSeconds, status.RecordingPaused);
    }

    private void RefreshNetwork()
    {
        try
        {
            var network = _systemInfo.SampleNetwork();
            _state.Update(s => s with { NetworkDownMbps = network.DownMbps, NetworkUpMbps = network.UpMbps });
        }
        catch { }
    }

    private async Task RefreshTailscale(CancellationToken ct)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(programFiles, "Tailscale", "tailscale.exe"),
            Path.Combine(programFilesX86, "Tailscale", "tailscale.exe"),
            "tailscale.exe"
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var executable in candidates)
        {
            try
            {
                if (Path.IsPathRooted(executable) && !File.Exists(executable))
                    continue;

                var result = await _process.RunAsync(executable, "status --json", ct);
                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                    continue;

                using var doc = JsonDocument.Parse(result.Output);
                var root = doc.RootElement;
                var backendRunning = root.TryGetProperty("BackendState", out var backendState)
                                     && backendState.GetString()?.Equals("Running", StringComparison.OrdinalIgnoreCase) == true;
                var selfOnline = root.TryGetProperty("Self", out var self)
                                 && self.TryGetProperty("Online", out var onlineProperty)
                                 && onlineProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
                                 && onlineProperty.GetBoolean();

                _state.Update(s => s with { Vpn = backendRunning || selfOnline ? "online" : "offline" });
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Tailscale status probe failed using {Executable}.", executable);
            }
        }

        _state.Update(s => s with { Vpn = "offline" });
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
}
