namespace TwinA.ControlServer.Models;

public sealed class AppConfiguration
{
    public List<CustomGameConfig> CustomGames { get; set; } = new();
    public List<GameProfileConfig> GameProfiles { get; set; } = new();
    public List<DevProjectConfig> DevProjects { get; set; } = new();
    public List<FlowConfig> Flows { get; set; } = new();
    public MqttConfig Mqtt { get; set; } = new();
    public UiPreferences Ui { get; set; } = new();
}

public sealed class GameProfileConfig
{
    public string GameId { get; set; } = "";
    public bool LaunchObs { get; set; }
    public bool LaunchDiscord { get; set; }
    public string? AudioOutputDeviceId { get; set; }
    public string? ObsScene { get; set; }
    public bool EnsureReplayBuffer { get; set; }
    public bool EnsureRecording { get; set; }
    public int? MasterVolume { get; set; }
}

public sealed class CustomGameConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom game";
    public string LaunchTarget { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? ProcessName { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ObsScene { get; set; }
    public bool StartReplayBuffer { get; set; }
    public bool StartRecording { get; set; }
}

public sealed class DevProjectConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Project";
    public string WorkingDirectory { get; set; } = "";
    public string SolutionOrProject { get; set; } = "";
    public string BuildCommand { get; set; } = "";
    public string TestCommand { get; set; } = "";
    public string RunCommand { get; set; } = "";
    public string IdePath { get; set; } = @"%LOCALAPPDATA%\Programs\Rider\bin\rider64.exe";
}

public sealed class FlowConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New flow";
    public string Category { get; set; } = "CUSTOM";
    public List<FlowStepConfig> Steps { get; set; } = new();
}

public sealed class FlowStepConfig
{
    public string Command { get; set; } = "";
    public Dictionary<string, string> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DelayAfterMs { get; set; }
    public bool ContinueOnError { get; set; }
}

public sealed class MqttConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1883;
    public bool Tls { get; set; }
    public string Username { get; set; } = "";
    // Password is intentionally never persisted here. Use TWINA_MQTT_PASSWORD.
    public List<MqttDeviceConfig> Devices { get; set; } = new();
}

public sealed class MqttDeviceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Device";
    public string StateTopic { get; set; } = "";
    public string CommandTopic { get; set; } = "";
    public string OnPayload { get; set; } = "ON";
    public string OffPayload { get; set; } = "OFF";
}

public sealed class UiPreferences
{
    public bool ConfirmPowerActions { get; set; } = true;
    public bool ProtectSystemPaths { get; set; } = true;
}
