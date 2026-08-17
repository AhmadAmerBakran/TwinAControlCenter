using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace TwinA.DesktopAgent.V07;

internal static class AudioSessionRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static DesktopV07Response GetSessions()
    {
        try
        {
            var sessions = ReadSessions();
            return DesktopV07Agent.Ok("Per-application audio sessions read from the current Windows output device.", JsonSerializer.Serialize(sessions, JsonOptions));
        }
        catch (Exception ex)
        {
            return DesktopV07Agent.Fail($"Windows audio sessions could not be read: {ex.Message}");
        }
    }

    internal static DesktopV07Response SetSession(JsonElement payload)
    {
        var pid = DesktopV07Agent.Int(payload, "pid");
        if (pid <= 0) return DesktopV07Agent.Fail("A valid audio-session process id is required.");

        double? requestedVolume = null;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("volume", out _))
            requestedVolume = Math.Clamp(DesktopV07Agent.Double(payload, "volume"), 0, 100);
        var requestedMute = DesktopV07Agent.BoolNullable(payload, "muted");
        if (requestedVolume is null && requestedMute is null)
            return DesktopV07Agent.Fail("No audio-session volume or mute change was supplied.");

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var collection = device.AudioSessionManager.Sessions;
            var changed = 0;

            for (var i = 0; i < collection.Count; i++)
            {
                var session = collection[i];
                int sessionPid;
                try { sessionPid = unchecked((int)session.GetProcessID); }
                catch { continue; }
                if (sessionPid != pid) continue;

                if (requestedVolume is not null)
                    session.SimpleAudioVolume.Volume = (float)(requestedVolume.Value / 100d);
                if (requestedMute is not null)
                    session.SimpleAudioVolume.Mute = requestedMute.Value;
                changed++;
            }

            if (changed == 0)
                return DesktopV07Agent.Fail("That process no longer has an active audio session on the default output device.");

            Thread.Sleep(80);
            var state = ReadSessions().FirstOrDefault(s => s.Pid == pid);
            if (state is null)
                return DesktopV07Agent.Fail("The audio change was sent, but the process audio session disappeared before TWIN A could verify it.");

            if (requestedVolume is not null && Math.Abs(state.Volume - requestedVolume.Value) > 1.5)
                return DesktopV07Agent.Fail($"Windows reported {state.Volume:0}% after the request instead of {requestedVolume.Value:0}%.", JsonSerializer.Serialize(state, JsonOptions));
            if (requestedMute is not null && state.Muted != requestedMute.Value)
                return DesktopV07Agent.Fail("Windows audio mute state did not match the requested state.", JsonSerializer.Serialize(state, JsonOptions));

            return DesktopV07Agent.Ok($"{state.DisplayName} audio state updated and verified.", JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            return DesktopV07Agent.Fail($"Windows audio session could not be changed: {ex.Message}");
        }
    }

    private static List<AppAudioSessionState> ReadSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var collection = device.AudioSessionManager.Sessions;
        var raw = new List<(int Pid, string ProcessName, string DisplayName, double Volume, bool Muted)>();

        for (var i = 0; i < collection.Count; i++)
        {
            var session = collection[i];
            int pid;
            try { pid = unchecked((int)session.GetProcessID); }
            catch { continue; }
            if (pid <= 0) continue;

            string processName = $"PID {pid}";
            string displayName = processName;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
                displayName = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? processName : process.MainWindowTitle;
            }
            catch { }

            try
            {
                raw.Add((
                    pid,
                    processName,
                    displayName,
                    Math.Round(session.SimpleAudioVolume.Volume * 100d, 1),
                    session.SimpleAudioVolume.Mute));
            }
            catch { }
        }

        return raw.GroupBy(x => x.Pid)
            .Select(group => new AppAudioSessionState(
                group.Key,
                group.First().ProcessName,
                group.First().DisplayName,
                Math.Round(group.Average(x => x.Volume), 1),
                group.All(x => x.Muted),
                group.Count()))
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed record AppAudioSessionState(
    int Pid,
    string ProcessName,
    string DisplayName,
    double Volume,
    bool Muted,
    int SessionCount);
