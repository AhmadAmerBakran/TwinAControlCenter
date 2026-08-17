using System.Diagnostics;
using System.Text.Json;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class CommandDispatcher
{
    private readonly ControlState _state;
    private readonly ProcessRunner _process;
    private readonly SessionMarkerService _markers;
    private readonly DesktopAgentClient _agent;
    private readonly ObsWebSocketClient _obs;
    private readonly IConfiguration _configuration;
    private readonly SteamLibraryService _steam;
    private readonly FileWorkspaceService _files;
    private readonly DevWorkspaceService _dev;
    private readonly SoundboardService _sounds;
    private readonly SettingsStore _settings;
    private readonly MqttRuntimeService _mqtt;
    private readonly ILogger<CommandDispatcher> _log;

    public CommandDispatcher(ControlState state, ProcessRunner process, SessionMarkerService markers, DesktopAgentClient agent,
        ObsWebSocketClient obs, IConfiguration configuration, SteamLibraryService steam, FileWorkspaceService files,
        DevWorkspaceService dev, SoundboardService sounds, SettingsStore settings, MqttRuntimeService mqtt, ILogger<CommandDispatcher> log)
    {
        _state = state; _process = process; _markers = markers; _agent = agent; _obs = obs; _configuration = configuration;
        _steam = steam; _files = files; _dev = dev; _sounds = sounds; _settings = settings; _mqtt = mqtt; _log = log;
    }

    public async Task<CommandResult> DispatchAsync(string command, JsonElement body, CancellationToken ct)
    {
        try
        {
            switch (command)
            {
                case "obs.record.toggle": return FromObs(command, await _obs.ToggleRecordingAsync(ct));
                case "obs.record.start": return FromObs(command, await _obs.SetRecordingAsync(true, ct));
                case "obs.record.stop": return FromObs(command, await _obs.SetRecordingAsync(false, ct));
                case "obs.replay.toggle": return FromObs(command, await _obs.ToggleReplayBufferAsync(ct));
                case "obs.replay.start": return FromObs(command, await _obs.SetReplayBufferAsync(true, ct));
                case "obs.replay.stop": return FromObs(command, await _obs.SetReplayBufferAsync(false, ct));
                case "obs.replay.save": return FromObs(command, await _obs.SaveReplayAsync(ct));
                case "obs.record.pause": return FromObs(command, await _obs.ToggleRecordPauseAsync(ct));
                case "obs.scene.set":
                {
                    var scene = GetString(body,"scene");
                    return string.IsNullOrWhiteSpace(scene) ? CommandResult.Failure(command,"No OBS scene name was supplied.") : FromObs(command, await _obs.SetSceneAsync(scene,ct));
                }
                case "obs.input.mute.toggle":
                {
                    var input = GetString(body,"inputName") ?? GetString(body,"input");
                    return string.IsNullOrWhiteSpace(input) ? CommandResult.Failure(command,"No OBS audio input name was supplied.") : FromObs(command,await _obs.ToggleInputMuteAsync(input,ct));
                }
                case "obs.mic.toggle": return FromObs(command, await _obs.ToggleInputMuteAsync(_configuration["TwinA:Obs:MicInput"] ?? "Mic/Aux",ct));
                case "obs.desktop.toggle": return FromObs(command, await _obs.ToggleInputMuteAsync(_configuration["TwinA:Obs:DesktopInput"] ?? "Desktop Audio",ct));

                case "app.launch":
                {
                    var app=(GetString(body,"app")??"").ToLowerInvariant();
                    return app switch
                    {
                        "obs" => await LaunchObsAsync(command,ct),
                        "steam" => await LaunchAndVerifyAsync(command,"steam","steam://open/main",null,ct),
                        "discord" => await LaunchAndVerifyAsync(command,"Discord","discord://-/channels/@me",null,ct),
                        "taskmgr" => await LaunchAndVerifyAsync(command,"Taskmgr","taskmgr.exe",null,ct),
                        "rider" => await LaunchRiderAsync(command,ct),
                        _ => CommandResult.Failure(command,$"Application '{app}' is not allowlisted. Nothing was launched.")
                    };
                }

                case "game.library.open":
                {
                    var r=await _steam.OpenLibraryAsync(ct); return FromTuple(command,r);
                }
                case "game.launch":
                {
                    var id=GetString(body,"id");
                    if(string.IsNullOrWhiteSpace(id)) return CommandResult.Failure(command,"No game id was supplied.");
                    var r=await _steam.LaunchAsync(id,ct);
                    if(!r.Ok) return FromTuple(command,r);
                    _state.Update(s=>s with{CurrentGame=GetString(body,"name")??s.CurrentGame});
                    var profileResult=await ApplyGameProfileAsync(id,ct);
                    if(profileResult is null) return FromTuple(command,r);
                    var verified=r.Verified && profileResult.Value.Verified;
                    var message=$"{r.Message} {profileResult.Value.Message}";
                    return verified?CommandResult.Success(command,message,r.Data):CommandResult.Executed(command,message,r.Data);
                }

                case "windows.lock": _process.Open("rundll32.exe","user32.dll,LockWorkStation"); return CommandResult.Executed(command,"Windows accepted the lock command. The lock screen cannot be verified from the session being locked.");
                case "windows.shutdown": _process.Open("shutdown.exe","/s /t 0"); return CommandResult.Executed(command,"Windows accepted the shutdown command.");
                case "windows.restart": _process.Open("shutdown.exe","/r /t 0"); return CommandResult.Executed(command,"Windows accepted the restart command.");
                case "windows.sleep":
                {
                    var r=await _process.PowerShellAsync("Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Application]::SetSuspendState('Suspend',$false,$false)",ct);
                    return r.ExitCode==0?CommandResult.Executed(command,"Windows accepted the sleep command."):CommandResult.Failure(command,r.Error);
                }
                case "windows.screenshot":
                {
                    var r=await _agent.SendAsync(command,new(),ct); if(!r.Ok)return CommandResult.Failure(command,r.Message); _state.Update(s=>s with{LastScreenshotPath=r.Data}); return CommandResult.Success(command,r.Message,r.Data);
                }
                case "windows.screenshot.folder.open": case "windows.desktop.show":
                {
                    var r=await _agent.SendAsync(command,new(),ct); return r.Ok?CommandResult.Executed(command,r.Message,r.Data):CommandResult.Failure(command,r.Message);
                }

                case "audio.master.up": case "audio.master.down": case "audio.master.mute": case "audio.master.set":
                {
                    var payload=new Dictionary<string,object?>();
                    if(command=="audio.master.set") { var value=GetNumber(body,"value"); if(value is null)return CommandResult.Failure(command,"No volume value supplied."); payload["value"]=Math.Clamp((int)Math.Round(value.Value),0,100); }
                    var r=await _agent.SendAsync(command,payload,ct); if(!r.Ok)return CommandResult.Failure(command,r.Message); ApplyAudioState(r.Data); return CommandResult.Success(command,r.Message,r.Data);
                }
                case "audio.device.set":
                {
                    var id=GetString(body,"id"); var flow=GetString(body,"flow")??"render";
                    if(string.IsNullOrWhiteSpace(id))return CommandResult.Failure(command,"No Windows audio endpoint id was supplied.");
                    var r=await _agent.SendAsync(command,new(){{"id",id},{"flow",flow}},ct); return r.Ok?CommandResult.Success(command,r.Message,r.Data):CommandResult.Failure(command,r.Message);
                }
                case "sound.play":
                {
                    var id=GetString(body,"id"); if(string.IsNullOrWhiteSpace(id))return CommandResult.Failure(command,"No sound id was supplied.");
                    var path=_sounds.GetPath(id); var volume=(int)Math.Clamp(GetNumber(body,"volume")??80,0,100);
                    var r=await _agent.SendAsync(command,new(){{"path",path},{"volume",volume}},ct);
                    return r.Ok?CommandResult.Executed(command,r.Message,r.Data):CommandResult.Failure(command,r.Message);
                }
                case "discord.mute.toggle": case "discord.deafen.toggle": case "discord.soundboard.open":
                {
                    var r=await _agent.SendAsync(command,new(),ct); return r.Ok?CommandResult.Executed(command,r.Message,r.Data):CommandResult.Failure(command,r.Message);
                }

                case "session.marker.add":
                {
                    var kind=GetString(body,"kind")??"NOTE"; var note=GetString(body,"note"); var file=await _markers.AddAsync(kind,note,_state.Snapshot.RecordingSeconds,ct); return CommandResult.Success(command,$"{kind} marker persisted and the marker file change was verified.",file);
                }

                case "files.open": { var path=_files.OpenOnPc(RequireString(body,"path")); return CommandResult.Executed(command,$"Windows accepted the open request for an existing path. The receiving application/window cannot be independently verified.",path); }
                case "files.folder.create": return CommandResult.Success(command,$"Folder created and verified: {_files.CreateFolder(RequireString(body,"parent"),RequireString(body,"name"))}");
                case "files.rename": return CommandResult.Success(command,$"Item renamed and verified: {_files.Rename(RequireString(body,"path"),RequireString(body,"newName"))}");
                case "files.move": return CommandResult.Success(command,$"Item moved and verified: {_files.Move(RequireString(body,"path"),RequireString(body,"destination"))}");
                case "files.copy": return CommandResult.Success(command,$"Item copied and verified: {_files.Copy(RequireString(body,"path"),RequireString(body,"destination"))}");
                case "files.delete": _files.Delete(RequireString(body,"path")); return CommandResult.Success(command,"Item deleted and absence verified.");

                case "dev.execute":
                {
                    var id=RequireString(body,"id"); var action=RequireString(body,"action"); var r=await _dev.ExecuteAsync(id,action,ct); return FromTuple(command,r);
                }

                case "flow.run": return await RunFlowAsync(command,RequireString(body,"id"),ct);
                case "iot.toggle":
                {
                    var id = RequireString(body, "id");
                    var on = RequireBool(body, "on");
                    var r = await _mqtt.SetDeviceAsync(id, on, ct);
                    return FromTuple(command, r);
                }
                default: return CommandResult.Failure(command,"Unknown or disabled command. No action was performed.");
            }
        }
        catch(Exception ex) { _log.LogError(ex,"Command {Command} failed",command); return CommandResult.Failure(command,ex.Message); }
    }

    private async Task<(bool Verified,string Message)?> ApplyGameProfileAsync(string gameId,CancellationToken ct)
    {
        var profile=_settings.Get().GameProfiles.FirstOrDefault(p=>p.GameId.Equals(gameId,StringComparison.OrdinalIgnoreCase));
        if(profile is null)return null;
        var verified=true; var messages=new List<string>();

        async Task RecordAsync(string label, Func<Task<CommandResult>> action)
        {
            try
            {
                var result = await action();
                if (!result.Ok || !result.Verified) verified = false;
                messages.Add($"{label}: {result.Message}");
            }
            catch (Exception ex)
            {
                verified = false;
                messages.Add($"{label}: {ex.Message}");
            }
        }

        if(profile.LaunchObs)
            await RecordAsync("OBS", () => LaunchObsAsync("game.profile.obs", ct));
        if(profile.LaunchDiscord)
            await RecordAsync("Discord", () => LaunchAndVerifyAsync("game.profile.discord", "Discord", "discord://-/channels/@me", null, ct));

        if(!string.IsNullOrWhiteSpace(profile.AudioOutputDeviceId))
        {
            try
            {
                var a=await _agent.SendAsync("audio.device.set",new(){{"id",profile.AudioOutputDeviceId},{"flow","render"}},ct);
                if(a.Ok) messages.Add($"Audio output: {a.Message}"); else { verified=false; messages.Add($"Audio output: {a.Message}"); }
            }
            catch(Exception ex) { verified=false; messages.Add($"Audio output: {ex.Message}"); }
        }

        if(profile.MasterVolume is int volume)
        {
            try
            {
                var a=await _agent.SendAsync("audio.master.set",new(){{"value",Math.Clamp(volume,0,100)}},ct);
                if(a.Ok){ApplyAudioState(a.Data);messages.Add($"Volume: {Math.Clamp(volume,0,100)}% verified.");}else{verified=false;messages.Add($"Volume: {a.Message}");}
            }
            catch(Exception ex) { verified=false; messages.Add($"Volume: {ex.Message}"); }
        }

        if(!string.IsNullOrWhiteSpace(profile.ObsScene))
        {
            try
            {
                var o=await _obs.SetSceneAsync(profile.ObsScene,ct); verified&=o.Ok&&o.Verified; messages.Add($"Scene: {o.Message}");
            }
            catch(Exception ex){verified=false;messages.Add($"Scene: {ex.Message}");}
        }
        if(profile.EnsureReplayBuffer)
        {
            try
            {
                var o=await _obs.SetReplayBufferAsync(true,ct); verified&=o.Ok&&o.Verified; messages.Add($"Replay: {o.Message}");
            }
            catch(Exception ex){verified=false;messages.Add($"Replay: {ex.Message}");}
        }
        if(profile.EnsureRecording)
        {
            try
            {
                var o=await _obs.SetRecordingAsync(true,ct); verified&=o.Ok&&o.Verified; messages.Add($"Recording: {o.Message}");
            }
            catch(Exception ex){verified=false;messages.Add($"Recording: {ex.Message}");}
        }
        return (verified,$"Game profile — {string.Join(" ",messages)}");
    }

    private async Task<CommandResult> RunFlowAsync(string command,string id,CancellationToken ct)
    {
        var flow=_settings.Get().Flows.FirstOrDefault(f=>f.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
        if(flow is null)return CommandResult.Failure(command,"Flow not found.");
        var lines=new List<string>(); var verifiedAll=true; var completed=0;
        foreach(var step in flow.Steps)
        {
            if(step.Command.StartsWith("flow.",StringComparison.OrdinalIgnoreCase))return CommandResult.Failure(command,"Nested flows are blocked to prevent recursion.");
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(step.Payload));
            var result=await DispatchAsync(step.Command,doc.RootElement,ct); completed++;
            lines.Add($"{(result.Ok?(result.Verified?"✓":"~"):"✕")} {step.Command}: {result.Message}");
            verifiedAll &= result.Verified;
            if(!result.Ok && !step.ContinueOnError)return CommandResult.Failure(command,$"Flow '{flow.Name}' stopped at step {completed}/{flow.Steps.Count}.",string.Join("\n",lines));
            if(step.DelayAfterMs>0)await Task.Delay(Math.Min(step.DelayAfterMs,30000),ct);
        }
        return verifiedAll?CommandResult.Success(command,$"Flow '{flow.Name}' completed. {completed}/{flow.Steps.Count} steps verified.",string.Join("\n",lines))
            :CommandResult.Executed(command,$"Flow '{flow.Name}' completed, but one or more steps could not expose a verifiable final state.",string.Join("\n",lines));
    }

    private async Task<CommandResult> LaunchObsAsync(string command,CancellationToken ct)
    {
        var configured=_configuration["TwinA:Apps:ObsPath"]; var path=string.IsNullOrWhiteSpace(configured)?@"D:\SteamLibrary\steamapps\common\OBS Studio\bin\64bit\obs64.exe":Environment.ExpandEnvironmentVariables(configured);
        if(!File.Exists(path))return CommandResult.Failure(command,$"OBS executable was not found at {path}. Nothing was launched.");
        if(Process.GetProcessesByName("obs64").Length>0)return CommandResult.Success(command,"OBS Studio is already running.",path);
        _process.Open(path); return await WaitForProcessAsync(command,"obs64","OBS Studio",path,ct);
    }

    private async Task<CommandResult> LaunchRiderAsync(string command,CancellationToken ct)
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Rider","bin","rider64.exe");
        if(!File.Exists(path))return CommandResult.Failure(command,$"Rider executable was not found at {path}.");
        _process.Open(path); return await WaitForProcessAsync(command,"rider64","Rider",path,ct);
    }

    private async Task<CommandResult> LaunchAndVerifyAsync(string command,string processName,string target,string? arguments,CancellationToken ct)
    {
        if(Process.GetProcessesByName(processName).Length>0)return CommandResult.Success(command,$"{processName} is already running.");
        _process.Open(target,arguments); return await WaitForProcessAsync(command,processName,processName,null,ct);
    }

    private static async Task<CommandResult> WaitForProcessAsync(string command,string processName,string displayName,string? data,CancellationToken ct)
    {
        for(var i=0;i<24;i++){if(Process.GetProcessesByName(processName).Length>0)return CommandResult.Success(command,$"{displayName} is running and was verified by process state.",data);await Task.Delay(250,ct);} return CommandResult.Failure(command,$"Windows accepted the launch request, but TWIN A could not verify that {displayName} started.",data);
    }

    private static CommandResult FromObs(string command,ObsCommandOutcome outcome)=>!outcome.Ok?CommandResult.Failure(command,outcome.Message,outcome.Data):outcome.Verified?CommandResult.Success(command,outcome.Message,outcome.Data):CommandResult.Executed(command,outcome.Message,outcome.Data);
    private static CommandResult FromTuple(string command,(bool Ok,bool Verified,string Message,string? Data) r)=>!r.Ok?CommandResult.Failure(command,r.Message,r.Data):r.Verified?CommandResult.Success(command,r.Message,r.Data):CommandResult.Executed(command,r.Message,r.Data);

    private void ApplyAudioState(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return; using var doc=JsonDocument.Parse(json); var root=doc.RootElement; var volume=root.TryGetProperty("volume",out var v)?v.GetInt32():_state.Snapshot.MasterVolume; var muted=root.TryGetProperty("muted",out var m)&&m.GetBoolean(); _state.Update(s=>s with{Agent="online",MasterVolume=Math.Clamp(volume,0,100),MasterMuted=muted});
    }

    private static bool RequireBool(JsonElement body,string name)
    {
        if(body.ValueKind!=JsonValueKind.Object || !body.TryGetProperty(name,out var value)) throw new InvalidOperationException($"Missing {name}.");
        if(value.ValueKind==JsonValueKind.True)return true;
        if(value.ValueKind==JsonValueKind.False)return false;
        if(value.ValueKind==JsonValueKind.String && bool.TryParse(value.GetString(),out var parsed))return parsed;
        throw new InvalidOperationException($"Invalid {name}; expected true or false.");
    }

    private static string RequireString(JsonElement body,string name)=>GetString(body,name) is {Length:>0} value?value:throw new InvalidOperationException($"Missing {name}.");
    private static string? GetString(JsonElement body,string name)=>body.ValueKind==JsonValueKind.Object&&body.TryGetProperty(name,out var value)?value.GetString():null;
    private static double? GetNumber(JsonElement body,string name){if(body.ValueKind!=JsonValueKind.Object||!body.TryGetProperty(name,out var value))return null;if(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number))return number;return value.ValueKind==JsonValueKind.String&&double.TryParse(value.GetString(),out number)?number:null;}
}
