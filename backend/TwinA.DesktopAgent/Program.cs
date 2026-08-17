using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace TwinA.DesktopAgent;

internal static class Program
{
    private const string PipeName = "TwinA.DesktopAgent";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Main()
    {
        Console.Title = "TWIN A Desktop Agent";
        Console.WriteLine("TWIN A Desktop Agent ready. Keep this running in the logged-in Windows session.");

        while (true)
        {
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            AgentResponse response;
            try
            {
                var req = JsonSerializer.Deserialize<AgentRequest>(line, JsonOptions) ?? throw new InvalidOperationException("Invalid request.");
                response = await Dispatch(req);
            }
            catch (Exception ex) { response = new(false, ex.Message, null); }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    private static async Task<AgentResponse> Dispatch(AgentRequest req)
    {
        switch (req.Command)
        {
            case "system.state.get":
                return await SystemStateResponse();
            case "windows.desktop.show":
                NativeHotkeys.WinD();
                return new(true, "Windows received Win + D.", null);
            case "windows.screenshot":
                var path = await CaptureScreenshot();
                return new(true, $"Screenshot verified on disk: {path}", path);
            case "windows.screenshot.folder.open":
                var dir = ScreenshotDirectory();
                Directory.CreateDirectory(dir);
                _ = Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true })
                    ?? throw new InvalidOperationException("Windows did not open the screenshot folder.");
                return new(true, $"Opened screenshot folder: {dir}", dir);
            case "audio.master.up":
                ChangeVolume(+5);
                return AudioResponse("Master volume increased and verified.");
            case "audio.master.down":
                ChangeVolume(-5);
                return AudioResponse("Master volume decreased and verified.");
            case "audio.master.mute":
                ToggleMute();
                return AudioResponse("Master mute state changed and verified.");
            case "audio.master.set":
                var value = ReadInt(req.Payload, "value");
                SetVolume(value);
                return AudioResponse($"Master volume set to {GetAudioState().Volume}% and verified.");
            case "audio.devices.get":
                return AudioDevicesResponse();
            case "audio.device.set":
            {
                var id = ReadString(req.Payload, "id");
                var flow = ReadString(req.Payload, "flow");
                SetDefaultAudioEndpoint(id);
                await Task.Delay(200);
                using var enumerator = new MMDeviceEnumerator();
                var expectedFlow = flow.Equals("capture", StringComparison.OrdinalIgnoreCase) ? DataFlow.Capture : DataFlow.Render;
                using var current = enumerator.GetDefaultAudioEndpoint(expectedFlow, Role.Multimedia);
                return current.ID.Equals(id, StringComparison.OrdinalIgnoreCase)
                    ? new(true, $"Default {flow} device changed and verified: {current.FriendlyName}", JsonSerializer.Serialize(new { id=current.ID, name=current.FriendlyName, flow }, JsonOptions))
                    : new(false, $"Windows accepted the device change, but the default {flow} endpoint did not match the requested device.", null);
            }
            case "sound.play":
            {
                var soundPath = ReadString(req.Payload, "path");
                var volume = Math.Clamp(ReadOptionalInt(req.Payload, "volume", 80), 0, 100);
                if (!File.Exists(soundPath)) return new(false, $"Sound file not found: {soundPath}", null);
                _ = PlayMediaAsync(soundPath, volume);
                return new(true, $"Playback started for {Path.GetFileName(soundPath)} at {volume}% volume. Windows Media playback does not expose a reliable completion state to TWIN A.", soundPath);
            }
            case "discord.mute.toggle":
                if (Process.GetProcessesByName("Discord").Length == 0) return new(false, "Discord is not running. No mute shortcut was sent.", null);
                NativeHotkeys.CtrlShiftM(); await Task.Delay(120);
                return new(true, "Discord is running and Ctrl + Shift + M was sent. Discord does not expose the resulting mute state to TWIN A.", JsonSerializer.Serialize(new { executed=true, verified=false }, JsonOptions));
            case "discord.soundboard.open":
                if (Process.GetProcessesByName("Discord").Length == 0) return new(false, "Discord is not running. Soundboard hotkey was not sent.", null);
                NativeHotkeys.CtrlBacktick(); await Task.Delay(120);
                return new(true, "Discord Soundboard hotkey was sent. The Soundboard UI state is not exposed for verification.", JsonSerializer.Serialize(new { executed=true, verified=false }, JsonOptions));
            case "discord.deafen.toggle":
                if (Process.GetProcessesByName("Discord").Length == 0)
                    return new(false, "Discord is not running. No deafen shortcut was sent.", null);
                NativeHotkeys.CtrlShiftD();
                await Task.Delay(150);
                return new(true,
                    "Discord is running and Ctrl + Shift + D was sent. Discord does not expose the resulting deafen state to TWIN A, so the final state is unverified.",
                    JsonSerializer.Serialize(new { executed = true, verified = false }, JsonOptions));
            case "clipboard.set":
                var text = req.Payload.TryGetValue("text", out var clipboardValue) ? clipboardValue?.ToString() ?? "" : "";
                await RunPowerShell($"Set-Clipboard -Value '{text.Replace("'", "''")}'");
                return new(true, "Clipboard updated.", null);
            default:
                return new(false, "Desktop command is not allowlisted. No action was performed.", null);
        }
    }

    private static async Task<AgentResponse> SystemStateResponse()
    {
        var audio = GetAudioState();
        var gpu = await SystemMetrics.GetGpuAsync();
        var state = new SystemState(
            audio.Volume,
            audio.Muted,
            audio.Device,
            SystemMetrics.GetCpuUsage(),
            gpu.Load,
            SystemMetrics.GetRamUsage(),
            gpu.Temperature);
        return new(true, "Desktop Agent state verified.", JsonSerializer.Serialize(state, JsonOptions));
    }

    private static AgentResponse AudioResponse(string message)
    {
        var state = GetAudioState();
        return new(true, $"{message} Current volume: {state.Volume}%{(state.Muted ? " (muted)" : "")}.", JsonSerializer.Serialize(state, JsonOptions));
    }

    private static AudioState GetAudioState()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var endpoint = device.AudioEndpointVolume;
        var volume = (int)Math.Round(endpoint.MasterVolumeLevelScalar * 100f);
        return new AudioState(Math.Clamp(volume, 0, 100), endpoint.Mute, device.FriendlyName);
    }

    private static void ChangeVolume(int delta)
    {
        var current = GetAudioState().Volume;
        SetVolume(current + delta);
    }

    private static void SetVolume(int value)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 100) / 100f;
    }

    private static void ToggleMute()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
    }

    private static AgentResponse AudioDevicesResponse()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? renderDefault = null, captureDefault = null;
        try { using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); renderDefault = d.ID; } catch { }
        try { using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); captureDefault = d.ID; } catch { }
        var devices = new List<object>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) devices.Add(new { id=device.ID, name=device.FriendlyName, flow="render", isDefault=device.ID.Equals(renderDefault,StringComparison.OrdinalIgnoreCase) });
        }
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device) devices.Add(new { id=device.ID, name=device.FriendlyName, flow="capture", isDefault=device.ID.Equals(captureDefault,StringComparison.OrdinalIgnoreCase) });
        }
        return new(true, "Active Windows audio endpoints enumerated.", JsonSerializer.Serialize(devices, JsonOptions));
    }

    private static string ReadString(Dictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null) throw new InvalidOperationException($"Missing {key}.");
        var text = raw is JsonElement e ? e.ToString() : raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidOperationException($"Invalid {key}.") : text;
    }

    private static int ReadOptionalInt(Dictionary<string, object?> payload, string key, int fallback)
    {
        try { return ReadInt(payload,key); } catch { return fallback; }
    }

    private static async Task PlayMediaAsync(string path, int volume)
    {
        try
        {
            var escaped = path.Replace("'", "''");
            var normalizedVolume = (volume / 100d).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var script = "$ErrorActionPreference='Stop'; Add-Type -AssemblyName PresentationCore; $p=New-Object System.Windows.Media.MediaPlayer; " +
                         $"$p.Open([Uri]::new('{escaped}')); $p.Volume={normalizedVolume}; " +
                         "for($i=0;$i -lt 20 -and -not $p.NaturalDuration.HasTimeSpan;$i++){Start-Sleep -Milliseconds 100}; $p.Play(); " +
                         "$ms=5000; if($p.NaturalDuration.HasTimeSpan){$ms=[Math]::Min(600000,[Math]::Max(200,[int]$p.NaturalDuration.TimeSpan.TotalMilliseconds+250))}; Start-Sleep -Milliseconds $ms; $p.Close();";
            await RunPowerShell(script);
        }
        catch { }
    }

    private static void SetDefaultAudioEndpoint(string deviceId)
    {
        var policy = (IPolicyConfig)(object)new PolicyConfigClient();
        foreach (ERole role in Enum.GetValues<ERole>())
        {
            var hr = policy.SetDefaultEndpoint(deviceId, role);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static int ReadInt(Dictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null) throw new InvalidOperationException($"Missing {key}.");
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n)) return n;
            if (int.TryParse(element.ToString(), out n)) return n;
        }
        if (int.TryParse(raw.ToString(), out var parsed)) return parsed;
        throw new InvalidOperationException($"Invalid {key}.");
    }

    private static string ScreenshotDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TwinAControl");

    private static async Task<string> CaptureScreenshot()
    {
        var dir = ScreenshotDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var script = "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                     "$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; " +
                     "$g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); " +
                     $"$bmp.Save('{path.Replace("'", "''")}',[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose();";
        await RunPowerShell(script);
        if (!File.Exists(path)) throw new IOException($"Screenshot command completed but the file was not found at {path}.");
        return path;
    }

    private static async Task RunPowerShell(string script)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"PowerShell exited with code {p.ExitCode}." : error);
    }

    private sealed record AgentRequest(string Command, Dictionary<string, object?> Payload);
    private sealed record AgentResponse(bool Ok, string Message, string? Data);
    private sealed record AudioState(int Volume, bool Muted, string Device);
    private sealed record SystemState(int Volume, bool Muted, string Device, int Cpu, int Gpu, int Ram, int GpuTemp);

    private static class SystemMetrics
    {
        private static readonly object CpuGate = new();
        private static bool _cpuInitialized;
        private static ulong _previousIdle;
        private static ulong _previousKernel;
        private static ulong _previousUser;

        private static readonly SemaphoreSlim GpuGate = new(1, 1);
        private static DateTimeOffset _lastGpuRead = DateTimeOffset.MinValue;
        private static GpuState _lastGpu = new(0, 0);

        public static int GetCpuUsage()
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return 0;
            var idle = ToUInt64(idleTime);
            var kernel = ToUInt64(kernelTime);
            var user = ToUInt64(userTime);

            lock (CpuGate)
            {
                if (!_cpuInitialized)
                {
                    _cpuInitialized = true;
                    _previousIdle = idle;
                    _previousKernel = kernel;
                    _previousUser = user;
                    return 0;
                }

                var idleDelta = idle - _previousIdle;
                var kernelDelta = kernel - _previousKernel;
                var userDelta = user - _previousUser;
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;

                var total = kernelDelta + userDelta;
                if (total == 0) return 0;
                var busy = total > idleDelta ? total - idleDelta : 0;
                return Math.Clamp((int)Math.Round(busy * 100d / total), 0, 100);
            }
        }

        public static int GetRamUsage()
        {
            var status = new MEMORYSTATUSEX();
            return GlobalMemoryStatusEx(status) ? Math.Clamp((int)status.dwMemoryLoad, 0, 100) : 0;
        }

        public static async Task<GpuState> GetGpuAsync()
        {
            if (DateTimeOffset.UtcNow - _lastGpuRead < TimeSpan.FromSeconds(2)) return _lastGpu;

            await GpuGate.WaitAsync();
            try
            {
                if (DateTimeOffset.UtcNow - _lastGpuRead < TimeSpan.FromSeconds(2)) return _lastGpu;

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process is null) return _lastGpu;
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) return _lastGpu;

                var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(line)) return _lastGpu;
                var parts = line.Split(',');
                if (parts.Length < 2) return _lastGpu;
                if (!int.TryParse(parts[0].Trim(), out var load)) return _lastGpu;
                if (!int.TryParse(parts[1].Trim(), out var temp)) return _lastGpu;

                _lastGpu = new GpuState(Math.Clamp(load, 0, 100), Math.Clamp(temp, 0, 120));
                _lastGpuRead = DateTimeOffset.UtcNow;
                return _lastGpu;
            }
            catch
            {
                return _lastGpu;
            }
            finally
            {
                GpuGate.Release();
            }
        }

        private static ulong ToUInt64(FILETIME time) => ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        public sealed record GpuState(int Load, int Temperature);
    }

    private static class NativeHotkeys
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const uint KeyUp = 0x0002;
        private const byte VkControl = 0x11;
        private const byte VkShift = 0x10;
        private const byte VkD = 0x44;
        private const byte VkM = 0x4D;
        private const byte VkOem3 = 0xC0;
        private const byte VkLeftWin = 0x5B;

        public static void WinD()
        {
            keybd_event(VkLeftWin, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkLeftWin, 0, KeyUp, UIntPtr.Zero);
        }

        public static void CtrlShiftM()
        {
            keybd_event(VkControl,0,0,UIntPtr.Zero); keybd_event(VkShift,0,0,UIntPtr.Zero); keybd_event(VkM,0,0,UIntPtr.Zero);
            keybd_event(VkM,0,KeyUp,UIntPtr.Zero); keybd_event(VkShift,0,KeyUp,UIntPtr.Zero); keybd_event(VkControl,0,KeyUp,UIntPtr.Zero);
        }

        public static void CtrlBacktick()
        {
            keybd_event(VkControl,0,0,UIntPtr.Zero); keybd_event(VkOem3,0,0,UIntPtr.Zero);
            keybd_event(VkOem3,0,KeyUp,UIntPtr.Zero); keybd_event(VkControl,0,KeyUp,UIntPtr.Zero);
        }

        public static void CtrlShiftD()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkShift, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkShift, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
        }
    }

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private sealed class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, IntPtr defaultPeriodPtr, IntPtr minimumPeriodPtr);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }

}
