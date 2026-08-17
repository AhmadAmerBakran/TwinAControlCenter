namespace TwinA.ControlServer.Models;

public sealed record ObsAudioSourceState(string Name, bool Muted, double VolumeDb);

public sealed record ControlSnapshot(
    string Pc,
    string Vpn,
    string Agent,
    string Obs,
    bool Recording,
    bool RecordingPaused,
    int RecordingSeconds,
    string Scene,
    bool ReplayBuffer,
    bool StudioMode,
    string[] Scenes,
    ObsAudioSourceState[] ObsAudioSources,
    bool MicMuted,
    bool DesktopMuted,
    int Cpu,
    int Gpu,
    int Ram,
    int GpuTemp,
    double NetworkDownMbps,
    double NetworkUpMbps,
    string? CurrentGame,
    string SessionName,
    int MasterVolume,
    bool MasterMuted,
    string? LastScreenshotPath);

public sealed record CommandResult(bool Ok, bool Verified, string Message, string Command, DateTimeOffset Timestamp, string? Data = null)
{
    public static CommandResult Success(string command, string message, string? data = null) => new(true, true, message, command, DateTimeOffset.UtcNow, data);
    public static CommandResult Executed(string command, string message, string? data = null) => new(true, false, message, command, DateTimeOffset.UtcNow, data);
    public static CommandResult Failure(string command, string message, string? data = null) => new(false, false, message, command, DateTimeOffset.UtcNow, data);
}
