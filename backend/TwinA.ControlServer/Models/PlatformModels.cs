namespace TwinA.ControlServer.Models;

public sealed record SteamGameInfo(
    string Id,
    uint AppId,
    string Name,
    string InstallDirectory,
    string LibraryPath,
    string LaunchUri,
    bool Installed,
    bool Running,
    string? RunningProcess);

public sealed record AudioDeviceInfo(string Id, string Name, string Flow, bool IsDefault);
public sealed record SoundInfo(string Id, string Name, string FileName, string Extension, long Size, DateTimeOffset ModifiedAt);
public sealed record DriveInfoDto(string Name, string Label, string Format, long TotalBytes, long FreeBytes);
public sealed record FileEntryDto(string Name, string FullPath, bool IsDirectory, long Size, DateTimeOffset ModifiedAt, string Extension, bool Protected);
public sealed record DevProjectStatus(string Id, string Name, string WorkingDirectory, bool Exists, string Branch, int ChangedFiles, string GitSummary, string DotnetVersion, string NodeVersion, string GitVersion, string DockerVersion);
public sealed record NetworkInfoDto(string Name, string Description, string LinkSpeed, double DownMbps, double UpMbps);
public sealed record SystemDetailsDto(NetworkInfoDto Network, TimeSpan Uptime, DriveInfoDto[] Drives, string MachineName, string OsDescription);

public sealed record MqttDeviceStateDto(string Id, string Name, string? Value, bool Online, string StateTopic);
