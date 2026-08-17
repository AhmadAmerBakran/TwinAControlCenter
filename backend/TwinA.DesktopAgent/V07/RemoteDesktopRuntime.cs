using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace TwinA.DesktopAgent.V07;

internal static class RemoteDesktopRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static DesktopV07Response Capture(JsonElement payload)
    {
        var maxWidth = Math.Clamp(DesktopV07Agent.Int(payload, "maxWidth", 1600), 640, 2560);
        var quality = Math.Clamp(DesktopV07Agent.Int(payload, "quality", 62), 30, 90);
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0) return DesktopV07Agent.Fail("Windows reported an invalid virtual desktop size.");

            using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(source))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            var scale = bounds.Width > maxWidth ? maxWidth / (double)bounds.Width : 1d;
            var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));

            using var output = scale < 0.999
                ? new Bitmap(source, new Size(width, height))
                : new Bitmap(source);
            using var stream = new MemoryStream();
            SaveJpeg(output, stream, quality);

            var frame = new DesktopFrame(
                Convert.ToBase64String(stream.ToArray()),
                width,
                height,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                DateTimeOffset.UtcNow);
            return DesktopV07Agent.Ok("Virtual desktop frame captured.", JsonSerializer.Serialize(frame, JsonOptions));
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
                "leftclick" => Click(payload, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, "Left click sent to Windows."),
                "doubleclick" => DoubleClick(payload),
                "rightclick" => Click(payload, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, "Right click sent to Windows."),
                "wheel" => Wheel(payload),
                "key" => Key(payload),
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
        if (!KeyMap.TryGetValue(keyName, out var vk)) return DesktopV07Agent.Fail($"Unsupported remote key '{keyName}'.");
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return DesktopV07Agent.Ok($"Key '{keyName}' sent to Windows.");
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
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KEYEVENTF_UNICODE } }
                },
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
                }
            };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
                return DesktopV07Agent.Fail("Windows did not accept all remote text input events.");
        }
        return DesktopV07Agent.Ok("Text input sent to Windows.");
    }

    private static void MovePointer(JsonElement payload)
    {
        var x = Math.Clamp(DesktopV07Agent.Double(payload, "x", 0.5), 0, 1);
        var y = Math.Clamp(DesktopV07Agent.Double(payload, "y", 0.5), 0, 1);
        var bounds = SystemInformation.VirtualScreen;
        var screenX = bounds.Left + (int)Math.Round(x * Math.Max(0, bounds.Width - 1));
        var screenY = bounds.Top + (int)Math.Round(y * Math.Max(0, bounds.Height - 1));
        if (!SetCursorPos(screenX, screenY)) throw new InvalidOperationException("Windows refused the pointer move.");
    }

    private static void SaveJpeg(Image image, Stream target, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase));
        if (codec is null)
        {
            image.Save(target, ImageFormat.Jpeg);
            return;
        }
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        image.Save(target, codec, parameters);
    }

    private static readonly Dictionary<string, byte> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Escape"] = 0x1B,
        ["Enter"] = 0x0D,
        ["Tab"] = 0x09,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Space"] = 0x20,
        ["ArrowLeft"] = 0x25,
        ["ArrowUp"] = 0x26,
        ["ArrowRight"] = 0x27,
        ["ArrowDown"] = 0x28,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
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
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
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
