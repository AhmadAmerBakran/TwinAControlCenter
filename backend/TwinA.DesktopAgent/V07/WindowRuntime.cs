using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TwinA.DesktopAgent.V07;

internal static class WindowRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "dwm", "fontdrvhost",
        "TwinA.DesktopAgent", "TwinA.ControlServer"
    };

    internal static string GetRuntimeJson()
    {
        var windows = EnumerateWindows();
        var foregroundHandle = GetForegroundWindow();
        var foreground = windows.FirstOrDefault(w => w.Handle == foregroundHandle.ToInt64());
        int processCount;
        try { processCount = Process.GetProcesses().Length; } catch { processCount = 0; }

        var runtime = new DesktopRuntimeState(
            IsRunning("steam"),
            IsRunning("Discord"),
            IsRunning("obs64"),
            foreground?.Title ?? string.Empty,
            foreground?.ProcessName ?? string.Empty,
            foreground?.Pid ?? 0,
            windows.Count,
            processCount);
        return JsonSerializer.Serialize(runtime, JsonOptions);
    }

    internal static string GetWindowsJson()
        => JsonSerializer.Serialize(EnumerateWindows(), JsonOptions);

    internal static string GetProcessesJson()
    {
        var items = new List<DesktopProcessState>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var protectedProcess = IsProtected(process.Id, name);
                long memory = 0;
                try { memory = process.WorkingSet64; } catch { }
                string title = string.Empty;
                try { title = process.MainWindowTitle ?? string.Empty; } catch { }
                bool responding = true;
                try { responding = process.Responding; } catch { }
                items.Add(new DesktopProcessState(
                    process.Id,
                    name,
                    title,
                    memory,
                    !string.IsNullOrWhiteSpace(title),
                    responding,
                    protectedProcess));
            }
            catch { }
            finally { process.Dispose(); }
        }

        return JsonSerializer.Serialize(
            items.OrderByDescending(p => p.HasWindow)
                 .ThenByDescending(p => p.MemoryBytes)
                 .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                 .Take(300),
            JsonOptions);
    }

    internal static DesktopV07Response WindowAction(JsonElement payload)
    {
        var handleValue = DesktopV07Agent.Long(payload, "handle");
        var action = (DesktopV07Agent.String(payload, "action") ?? string.Empty).Trim().ToLowerInvariant();
        if (handleValue == 0) return DesktopV07Agent.Fail("A valid window handle is required.");
        var hwnd = new IntPtr(handleValue);
        if (!IsWindow(hwnd)) return DesktopV07Agent.Fail("That window no longer exists.");

        return action switch
        {
            "focus" => Focus(hwnd),
            "minimize" => ChangeState(hwnd, SW_MINIMIZE, () => IsIconic(hwnd), "Window minimized and state verified."),
            "maximize" => ChangeState(hwnd, SW_MAXIMIZE, () => IsZoomed(hwnd), "Window maximized and state verified."),
            "restore" => ChangeState(hwnd, SW_RESTORE, () => !IsIconic(hwnd) && !IsZoomed(hwnd), "Window restored and state verified."),
            "close" => Close(hwnd),
            _ => DesktopV07Agent.Fail($"Unsupported window action '{action}'.")
        };
    }

    internal static async Task<DesktopV07Response> EndProcessAsync(JsonElement payload)
    {
        var pid = DesktopV07Agent.Int(payload, "pid");
        if (pid <= 0) return DesktopV07Agent.Fail("A valid process id is required.");

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch { return DesktopV07Agent.Fail("That process is no longer running."); }

        using (process)
        {
            string name;
            try { name = process.ProcessName; }
            catch { return DesktopV07Agent.Fail("TWIN A could not inspect that process safely."); }

            if (IsProtected(pid, name))
                return DesktopV07Agent.Fail($"'{name}' is protected and cannot be ended from TWIN A.");

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                return DesktopV07Agent.Fail($"Windows refused to end '{name}': {ex.Message}");
            }

            for (var i = 0; i < 30; i++)
            {
                if (!ProcessExists(pid))
                    return DesktopV07Agent.Ok($"Process '{name}' ({pid}) ended and absence was verified.");
                await Task.Delay(100);
            }

            return DesktopV07Agent.Fail($"The end-task request was sent, but process '{name}' ({pid}) is still running.");
        }
    }

    private static DesktopV07Response Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        _ = SetForegroundWindow(hwnd);
        Thread.Sleep(120);
        return GetForegroundWindow() == hwnd
            ? DesktopV07Agent.Ok("Window focused and foreground state verified.")
            : DesktopV07Agent.Fail("Windows did not allow TWIN A to make that window foreground. Windows focus-stealing protection may have blocked it.");
    }

    private static DesktopV07Response ChangeState(IntPtr hwnd, int command, Func<bool> verify, string success)
    {
        _ = ShowWindow(hwnd, command);
        Thread.Sleep(120);
        return verify() ? DesktopV07Agent.Ok(success) : DesktopV07Agent.Fail("Windows accepted the window command, but the requested state could not be verified.");
    }

    private static DesktopV07Response Close(IntPtr hwnd)
    {
        _ = PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        for (var i = 0; i < 20; i++)
        {
            if (!IsWindow(hwnd)) return DesktopV07Agent.Ok("Window closed and its absence was verified.");
            Thread.Sleep(100);
        }
        return DesktopV07Agent.Fail("The close request was sent, but the window is still open. It may be waiting for a save/confirmation dialog.");
    }

    private static List<DesktopWindowState> EnumerateWindows()
    {
        var foreground = GetForegroundWindow();
        var result = new List<DesktopWindowState>();
        _ = EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return true;
                var length = GetWindowTextLength(hwnd);
                if (length <= 0) return true;
                var titleBuilder = new StringBuilder(length + 1);
                _ = GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title)) return true;

                _ = GetWindowThreadProcessId(hwnd, out var pidRaw);
                var pid = unchecked((int)pidRaw);
                var processName = string.Empty;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    processName = process.ProcessName;
                }
                catch { }

                result.Add(new DesktopWindowState(
                    hwnd.ToInt64(),
                    title,
                    processName,
                    pid,
                    hwnd == foreground,
                    IsIconic(hwnd),
                    IsZoomed(hwnd)));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return result.OrderByDescending(w => w.Foreground).ThenBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsProtected(int pid, string name)
        => pid <= 4 || pid == Environment.ProcessId || ProtectedProcesses.Contains(name);

    private static bool ProcessExists(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch { return false; }
    }

    private static bool IsRunning(string processName)
    {
        try { return Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}

internal sealed record DesktopRuntimeState(
    bool SteamRunning,
    bool DiscordRunning,
    bool ObsRunning,
    string ForegroundTitle,
    string ForegroundProcess,
    int ForegroundPid,
    int WindowCount,
    int ProcessCount);

internal sealed record DesktopWindowState(
    long Handle,
    string Title,
    string ProcessName,
    int Pid,
    bool Foreground,
    bool Minimized,
    bool Maximized);

internal sealed record DesktopProcessState(
    int Pid,
    string Name,
    string WindowTitle,
    long MemoryBytes,
    bool HasWindow,
    bool Responding,
    bool Protected);
