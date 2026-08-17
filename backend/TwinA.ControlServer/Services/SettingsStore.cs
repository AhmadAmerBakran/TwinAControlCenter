using System.Text.Json;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IWebHostEnvironment _environment;
    private AppConfiguration _settings;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore(IWebHostEnvironment environment)
    {
        _environment = environment;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinAControlCenter");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "config.json");
        _settings = Load();
        SeedDefaults();
        SaveInternal();
    }

    public string ConfigPath => _path;

    public AppConfiguration Get()
    {
        lock (_gate) return Clone(_settings);
    }

    public T Update<T>(Func<AppConfiguration, T> update)
    {
        lock (_gate)
        {
            var result = update(_settings);
            SaveInternal();
            return result;
        }
    }

    public AppConfiguration Replace(AppConfiguration settings)
    {
        lock (_gate)
        {
            _settings = settings ?? new AppConfiguration();
            SeedDefaults();
            SaveInternal();
            return Clone(_settings);
        }
    }

    private AppConfiguration Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfiguration();
            return JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(_path), JsonOptions) ?? new AppConfiguration();
        }
        catch { return new AppConfiguration(); }
    }

    private void SeedDefaults()
    {
        Normalize();
        if (!_settings.DevProjects.Any(p => p.Id.Equals("twina", StringComparison.OrdinalIgnoreCase)))
        {
            var root = FindProjectRoot(_environment.ContentRootPath);
            _settings.DevProjects.Insert(0, new DevProjectConfig
            {
                Id = "twina",
                Name = "TWIN A Control Center",
                WorkingDirectory = root,
                SolutionOrProject = Path.Combine(root, "TwinAControlCenter.sln"),
                BuildCommand = "powershell -ExecutionPolicy Bypass -File .\\scripts\\build.ps1",
                TestCommand = "dotnet test .\\TwinAControlCenter.sln --no-restore",
                RunCommand = "powershell -ExecutionPolicy Bypass -File .\\scripts\\run.ps1"
            });
        }

        if (_settings.Flows.Count == 0)
        {
            _settings.Flows.Add(new FlowConfig
            {
                Id = "recording-session", Name = "Start recording session", Category = "RECORDING",
                Steps = new()
                {
                    new() { Command = "app.launch", Payload = new(){{"app","obs"}}, DelayAfterMs = 1200 },
                    new() { Command = "obs.replay.start", ContinueOnError = true, DelayAfterMs = 300 },
                    new() { Command = "obs.record.start" }
                }
            });
            _settings.Flows.Add(new FlowConfig
            {
                Id = "end-session", Name = "End session", Category = "FINISH",
                Steps = new() { new() { Command = "obs.record.stop", ContinueOnError = true }, new() { Command = "obs.replay.stop", ContinueOnError = true } }
            });
        }
    }

    private void Normalize()
    {
        _settings.CustomGames ??= new();
        _settings.GameProfiles ??= new();
        _settings.DevProjects ??= new();
        _settings.Flows ??= new();
        _settings.Mqtt ??= new MqttConfig();
        _settings.Mqtt.Devices ??= new();
        _settings.Ui ??= new UiPreferences();
        foreach (var flow in _settings.Flows)
        {
            flow.Steps ??= new();
            foreach (var step in flow.Steps) step.Payload ??= new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FindProjectRoot(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        for (var i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TwinAControlCenter.sln"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private void SaveInternal()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_settings, JsonOptions));
        File.Move(temp, _path, true);
    }

    private static AppConfiguration Clone(AppConfiguration value)
        => JsonSerializer.Deserialize<AppConfiguration>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? new AppConfiguration();
}
