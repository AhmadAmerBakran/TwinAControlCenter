using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

namespace TwinA.Launcher;

internal static class Program
{
    private static Process? _server;
    private static Process? _agent;
    private static NotifyIcon? _tray;
    private static readonly Uri DashboardUri = new("http://127.0.0.1:5055");

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "TwinAControlCenter.Launcher", out var firstInstance);
        if (!firstInstance)
        {
            if (args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase))) OpenDashboard();
            return;
        }

        ApplicationConfiguration.Initialize();
        StartServices();
        CreateTrayIcon();

        if (args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase)))
            _ = OpenDashboardWhenReadyAsync();

        Application.ApplicationExit += (_, _) => StopServices();
        Application.Run();
    }

    private static void StartServices()
    {
        var installRoot = AppContext.BaseDirectory;
        var serverExe = Path.GetFullPath(Path.Combine(installRoot, "..", "server", "TwinA.ControlServer.exe"));
        var agentExe = Path.GetFullPath(Path.Combine(installRoot, "..", "agent", "TwinA.DesktopAgent.exe"));

        if (!File.Exists(serverExe) || !File.Exists(agentExe))
        {
            MessageBox.Show(
                "TWIN A installation is incomplete. Please repair or reinstall TWIN A Control Center.",
                "TWIN A Control Center",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _agent = StartChild(agentExe);
        Thread.Sleep(500);
        _server = StartChild(serverExe);
    }

    private static Process? StartChild(string executable)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"TWIN A could not start {Path.GetFileName(executable)}.\n\n{ex.Message}",
                "TWIN A Control Center",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
    }

    private static void CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open TWIN A", null, (_, _) => OpenDashboard());
        menu.Items.Add("Open Tailscale", null, (_, _) => OpenTailscale());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart TWIN A", null, (_, _) => RestartServices());
        menu.Items.Add("Exit TWIN A", null, (_, _) => Application.Exit());

        _tray = new NotifyIcon
        {
            Text = "TWIN A Control Center",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenDashboard();
        Application.ApplicationExit += (_, _) =>
        {
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        };
    }

    private static async Task OpenDashboardWhenReadyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
        for (var i = 0; i < 24; i++)
        {
            try
            {
                using var response = await http.GetAsync(new Uri(DashboardUri, "/api/health"));
                if (response.IsSuccessStatusCode)
                {
                    OpenDashboard();
                    return;
                }
            }
            catch { }
            await Task.Delay(250);
        }
        OpenDashboard();
    }

    private static void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = DashboardUri.ToString(), UseShellExecute = true });
        }
        catch { }
    }

    private static void OpenTailscale()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale-ipn.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            MessageBox.Show("Tailscale is not installed. Run the TWIN A installer again and select Tailscale, or install it from tailscale.com.", "TWIN A Control Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = true }); } catch { }
    }

    private static void RestartServices()
    {
        StopServices();
        StartServices();
    }

    private static void StopServices()
    {
        StopChild(_server);
        StopChild(_agent);
        _server = null;
        _agent = null;
    }

    private static void StopChild(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2500);
            }
        }
        catch { }
        finally { process.Dispose(); }
    }
}
