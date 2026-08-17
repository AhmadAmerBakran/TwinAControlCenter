using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class ControlState
{
    private readonly object _gate = new();
    private DateTimeOffset? _recordingStarted;
    private ControlSnapshot _snapshot = new(
        "online", "offline", "offline", "offline", false, false, 0, "—", false, false,
        Array.Empty<string>(), Array.Empty<ObsAudioSourceState>(), false, false,
        0, 0, 0, 0, 0, 0, null, "TWIN A Control Center", 0, false, null);

    public ControlSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var seconds = _recordingStarted is null ? _snapshot.RecordingSeconds : (int)(DateTimeOffset.UtcNow - _recordingStarted.Value).TotalSeconds;
                return _snapshot with { RecordingSeconds = Math.Max(0, seconds) };
            }
        }
    }

    public ControlSnapshot Update(Func<ControlSnapshot, ControlSnapshot> update)
    {
        lock (_gate)
        {
            _snapshot = update(_snapshot);
            return Snapshot;
        }
    }

    public void SetRecording(bool recording, int? verifiedSeconds = null, bool? paused = null)
    {
        lock (_gate)
        {
            if (recording)
            {
                var seconds = Math.Max(0, verifiedSeconds ?? _snapshot.RecordingSeconds);
                var isPaused = paused ?? _snapshot.RecordingPaused;
                _recordingStarted = isPaused ? null : DateTimeOffset.UtcNow.AddSeconds(-seconds);
                _snapshot = _snapshot with
                {
                    Recording = true,
                    RecordingPaused = isPaused,
                    RecordingSeconds = seconds
                };
            }
            else
            {
                var elapsed = verifiedSeconds ?? (_recordingStarted is null
                    ? _snapshot.RecordingSeconds
                    : (int)(DateTimeOffset.UtcNow - _recordingStarted.Value).TotalSeconds);
                _snapshot = _snapshot with { Recording = false, RecordingPaused = false, RecordingSeconds = Math.Max(0, elapsed) };
                _recordingStarted = null;
            }
        }
    }
}
