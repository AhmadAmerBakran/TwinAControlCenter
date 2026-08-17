using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace TwinA.DesktopAgent.V08;

internal static class MonitorRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string GetMonitorsJson()
        => JsonSerializer.Serialize(GetMonitors(), JsonOptions);

    internal static List<DesktopMonitorState> GetMonitors()
    {
        var screens = Screen.AllScreens;
        var result = new List<DesktopMonitorState>();
        var virtualBounds = SystemInformation.VirtualScreen;
        result.Add(new DesktopMonitorState(
            "all",
            screens.Length > 1 ? $"All displays · {screens.Length}" : "All displays",
            virtualBounds.Left,
            virtualBounds.Top,
            virtualBounds.Width,
            virtualBounds.Height,
            screens.Any(s => s.Primary),
            screens.Length));

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            result.Add(new DesktopMonitorState(
                $"screen-{i}",
                screen.Primary ? $"Display {i + 1} · Primary" : $"Display {i + 1}",
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Primary,
                1));
        }
        return result;
    }

    internal static Rectangle ResolveBounds(string? monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId) || monitorId.Equals("all", StringComparison.OrdinalIgnoreCase))
            return SystemInformation.VirtualScreen;

        if (monitorId.StartsWith("screen-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(monitorId.AsSpan(7), out var index))
        {
            var screens = Screen.AllScreens;
            if (index >= 0 && index < screens.Length) return screens[index].Bounds;
        }

        return SystemInformation.VirtualScreen;
    }

    internal static CapturedDesktopFrame CaptureJpeg(string? monitorId, int maxWidth, int quality)
    {
        var bounds = ResolveBounds(monitorId);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Windows reported an invalid desktop capture area.");

        maxWidth = Math.Clamp(maxWidth, 640, 4096);
        quality = Math.Clamp(quality, 25, 92);

        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            DrawCursor(graphics, bounds);
        }

        var scale = bounds.Width > maxWidth ? maxWidth / (double)bounds.Width : 1d;
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        using var output = scale < 0.999
            ? new Bitmap(source, new Size(width, height))
            : new Bitmap(source);
        using var stream = new MemoryStream();
        SaveJpeg(output, stream, quality);
        return new CapturedDesktopFrame(stream.ToArray(), width, height, bounds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static void DrawCursor(Graphics graphics, Rectangle captureBounds)
    {
        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursorInfo) || cursorInfo.flags != CURSOR_SHOWING || cursorInfo.hCursor == IntPtr.Zero) return;
        if (!captureBounds.Contains(cursorInfo.ptScreenPos.X, cursorInfo.ptScreenPos.Y)) return;

        var iconInfo = new ICONINFO();
        var hotspotX = 0;
        var hotspotY = 0;
        if (GetIconInfo(cursorInfo.hCursor, ref iconInfo))
        {
            hotspotX = unchecked((int)iconInfo.xHotspot);
            hotspotY = unchecked((int)iconInfo.yHotspot);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
        }

        var x = cursorInfo.ptScreenPos.X - captureBounds.Left - hotspotX;
        var y = cursorInfo.ptScreenPos.Y - captureBounds.Top - hotspotY;
        var hdc = graphics.GetHdc();
        try { _ = DrawIconEx(hdc, x, y, cursorInfo.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
        finally { graphics.ReleaseHdc(hdc); }
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

    private const int CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, ref ICONINFO piconinfo);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
}

internal sealed record DesktopMonitorState(
    string Id,
    string Name,
    int Left,
    int Top,
    int Width,
    int Height,
    bool Primary,
    int ScreenCount);

internal sealed record CapturedDesktopFrame(
    byte[] Bytes,
    int Width,
    int Height,
    Rectangle SourceBounds,
    long CapturedAtUnixMs);
