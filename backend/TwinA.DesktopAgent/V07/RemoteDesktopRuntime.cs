using System.Runtime.InteropServices;
using System.Text.Json;
using TwinA.DesktopAgent.V08;

namespace TwinA.DesktopAgent.V07;

internal static class RemoteDesktopRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static DesktopV07Response Capture(JsonElement payload)
    {
        var maxWidth = Math.Clamp(DesktopV07Agent.Int(payload, "maxWidth", 1600), 640, 4096);
        var quality = Math.Clamp(DesktopV07Agent.Int(payload, "quality", 62), 25, 92);
        var monitorId = DesktopV07Agent.String(payload, "monitorId") ?? "all";
        try
        {
            var captured = MonitorRuntime.CaptureJpeg(monitorId, maxWidth, quality);
            var frame = new DesktopFrame(
                Convert.ToBase64String(captured.Bytes),
                captured.Width,
                captured.Height,
                captured.SourceBounds.Left,
                captured.SourceBounds.Top,
                captured.SourceBounds.Width,
                captured.SourceBounds.Height,
                DateTimeOffset.FromUnixTimeMilliseconds(captured.CapturedAtUnixMs));
            return DesktopV07Agent.Ok("Desktop frame captured.", JsonSerializer.Serialize(frame, JsonOptions));
        }
        catch (Exception ex)
        {
            return DesktopV07Agent.Fail($"Desktop frame capture failed: {ex.Message}");
        }
    }

    internal static DesktopV07Response Input(JsonElement payload)
    {
        var action = (DesktopV07Agent.String(payload, "action") ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            return action switch
            {
                "move" => Move(payload),
                "leftdown" => MouseButton(payload, MOUSEEVENTF_LEFTDOWN, "Left mouse button down sent to Windows."),
                "leftup" => MouseButton(payload, MOUSEEVENTF_LEFTUP, "Left mouse button up sent to Windows."),
                "leftclick" => Click(payload, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, "Left click sent to Windows."),
                "doubleclick" => DoubleClick(payload),
                "rightclick" => Click(payload, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, "Right click sent to Windows."),
                "wheel" => Wheel(payload),
                "key" => Key(payload),
                "shortcut" => Shortcut(payload),
                "text" => Text(payload),
                _ => DesktopV07Agent.Fail($"Unsupported remote input action '{action}'.")
            };
        }
        catch (Exception ex)
        {
            return DesktopV07Agent.Fail($"Remote input failed: {ex.Message}");
        }
    }

    private static DesktopV07Response Move(JsonElement payload)
    {
        MovePointer(payload);
        return DesktopV07Agent.Ok("Pointer move sent to Windows.");
    }

    private static DesktopV07Response MouseButton(JsonElement payload, uint flag, string message)
    {
        MovePointer(payload);
        mouse_event(flag, 0, 0, 0, UIntPtr.Zero);
        return DesktopV07Agent.Ok(message);
    }

    private static DesktopV07Response Click(JsonElement payload, uint down, uint up, string message)
    {
        MovePointer(payload);
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
        return DesktopV07Agent.Ok(message);
    }

    private static DesktopV07Response DoubleClick(JsonElement payload)
    {
        MovePointer(payload);
        for (var i = 0; i < 2; i++)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            if (i == 0) Thread.Sleep(70);
        }
        return DesktopV07Agent.Ok("Double click sent to Windows.");
    }

    private static DesktopV07Response Wheel(JsonElement payload)
    {
        MovePointer(payload);
        var delta = DesktopV07Agent.Int(payload, "delta");
        if (delta == 0) delta = 120;
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
        return DesktopV07Agent.Ok("Mouse wheel input sent to Windows.");
    }

    private static DesktopV07Response Key(JsonElement payload)
    {
        var keyName = DesktopV07Agent.String(payload, "key") ?? string.Empty;
        if (!TryResolveKey(keyName, out var vk)) return DesktopV07Agent.Fail($"Unsupported remote key '{keyName}'.");
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return DesktopV07Agent.Ok($"Key '{keyName}' sent to Windows.");
    }

    private static DesktopV07Response Shortcut(JsonElement payload)
    {
        var shortcut = (DesktopV07Agent.String(payload, "shortcut") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(shortcut)) return DesktopV07Agent.Fail("No keyboard shortcut was supplied.");
        if (shortcut.Equals("CTRL+ALT+DELETE", StringComparison.OrdinalIgnoreCase))
            return DesktopV07Agent.Fail("Ctrl+Alt+Delete belongs to the Windows secure desktop and cannot be generated by a normal desktop application.");

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = new List<byte>();
        foreach (var part in parts)
        {
            if (!TryResolveKey(part, out var key)) return DesktopV07Agent.Fail($"Unsupported shortcut key '{part}'.");
            keys.Add(key);
        }
        if (keys.Count == 0 || keys.Count > 5) return DesktopV07Agent.Fail("Keyboard shortcuts must contain between one and five keys.");

        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        for (var i = keys.Count - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return DesktopV07Agent.Ok($"Shortcut '{shortcut}' sent to Windows.");
    }

    private static DesktopV07Response Text(JsonElement payload)
    {
        var text = DesktopV07Agent.String(payload, "text") ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return DesktopV07Agent.Fail("No text was supplied for remote typing.");
        if (text.Length > 1000) return DesktopV07Agent.Fail("Remote text input is limited to 1000 characters per request.");

        foreach (var character in text)
        {
            var inputs = new[]
            {
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KEYEVENTF_UNICODE } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } }
            };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != (uint)inputs.Length)
                return DesktopV07Agent.Fail("Windows did not accept all remote text input events.");
        }
        return DesktopV07Agent.Ok("Text input sent to Windows.");
    }

    private static void MovePointer(JsonElement payload)
    {
        var x = Math.Clamp(DesktopV07Agent.Double(payload, "x", 0.5), 0, 1);
        var y = Math.Clamp(DesktopV07Agent.Double(payload, "y", 0.5), 0, 1);
        var monitorId = DesktopV07Agent.String(payload, "monitorId") ?? "all";
        var bounds = MonitorRuntime.ResolveBounds(monitorId);
        var screenX = bounds.Left + (int)Math.Round(x * Math.Max(0, bounds.Width - 1));
        var screenY = bounds.Top + (int)Math.Round(y * Math.Max(0, bounds.Height - 1));
        if (!SetCursorPos(screenX, screenY)) throw new InvalidOperationException("Windows refused the pointer move.");
    }

    private static bool TryResolveKey(string keyName, out byte key)
    {
        if (KeyMap.TryGetValue(keyName, out key)) return true;
        if (keyName.Length == 1)
        {
            var ch = char.ToUpperInvariant(keyName[0]);
            if (ch is >= 'A' and <= 'Z') { key = (byte)ch; return true; }
            if (ch is >= '0' and <= '9') { key = (byte)ch; return true; }
        }
        key = 0;
        return false;
    }

    private static readonly Dictionary<string, byte> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["ALT"] = 0x12, ["SHIFT"] = 0x10, ["WIN"] = 0x5B, ["WINDOWS"] = 0x5B,
        ["Escape"] = 0x1B, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Space"] = 0x20,
        ["ArrowLeft"] = 0x25, ["Left"] = 0x25, ["ArrowUp"] = 0x26, ["Up"] = 0x26, ["ArrowRight"] = 0x27, ["Right"] = 0x27, ["ArrowDown"] = 0x28, ["Down"] = 0x28,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74, ["F6"] = 0x75,
        ["F7"] = 0x76, ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B
    };

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint INPUT_KEYBOARD = 1;

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
}

internal sealed record DesktopFrame(
    string Base64,
    int Width,
    int Height,
    int VirtualLeft,
    int VirtualTop,
    int VirtualWidth,
    int VirtualHeight,
    DateTimeOffset CapturedAt);
