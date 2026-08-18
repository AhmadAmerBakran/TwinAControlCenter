using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            if (args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase))) OpenTailscale();
            return;
        }

        ApplicationConfiguration.Initialize();
        StartServices();
        CreateTrayIcon();

        if (args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase)))
            _ = ConfigureIpadAccessAsync();
        else if (args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase)))
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
        menu.Items.Add("Configure iPad Access", null, (_, _) => _ = ConfigureIpadAccessAsync());
        menu.Items.Add("Configure OBS Password", null, (_, _) => ConfigureObsPassword());
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
        try { Process.Start(new ProcessStartInfo { FileName = DashboardUri.ToString(), UseShellExecute = true }); }
        catch { }
    }

    private static void ConfigureObsPassword()
    {
        using var form = new Form
        {
            Text = "TWIN A — OBS WebSocket Password",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(470, 185),
            ShowInTaskbar = true
        };

        var intro = new Label
        {
            Left = 18,
            Top = 16,
            Width = 430,
            Height = 42,
            Text = "Enter the password configured in OBS → Tools → WebSocket Server Settings. It is stored only in your Windows user environment and is never written into the TWIN A installation or GitHub repository."
        };
        var password = new TextBox
        {
            Left = 18,
            Top = 72,
            Width = 430,
            UseSystemPasswordChar = true
        };
        var save = new Button
        {
            Text = "Save",
            Left = 274,
            Top = 120,
            Width = 82,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "Cancel",
            Left = 366,
            Top = 120,
            Width = 82,
            DialogResult = DialogResult.Cancel
        };

        form.Controls.AddRange([intro, password, save, cancel]);
        form.AcceptButton = save;
        form.CancelButton = cancel;

        if (form.ShowDialog() != DialogResult.OK) return;
        var value = password.Text;
        if (string.IsNullOrWhiteSpace(value))
        {
            var remove = MessageBox.Show(
                "The password field is empty. Remove the currently stored TWIN A OBS password?",
                "TWIN A — OBS Password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (remove != DialogResult.Yes) return;
            Environment.SetEnvironmentVariable("TWINA_OBS_PASSWORD", null, EnvironmentVariableTarget.User);
        }
        else
        {
            Environment.SetEnvironmentVariable("TWINA_OBS_PASSWORD", value, EnvironmentVariableTarget.User);
        }

        RestartServices();
        MessageBox.Show(
            "OBS WebSocket password saved locally for this Windows user. TWIN A was restarted so the new value is active.",
            "TWIN A — OBS Password",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string? TailscaleCliPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TailscaleGuiPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale-ipn.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale-ipn.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void OpenTailscale()
    {
        var executable = TailscaleGuiPath() ?? TailscaleCliPath();
        if (executable is null)
        {
            MessageBox.Show(
                "Tailscale is not installed. Run the TWIN A installer again and select Tailscale, or install it from tailscale.com.",
                "TWIN A Control Center",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = true }); } catch { }
    }

    private static async Task ConfigureIpadAccessAsync()
    {
        var tailscale = TailscaleCliPath();
        if (tailscale is null)
        {
            MessageBox.Show(
                "Tailscale is required for the easiest private iPad connection. Re-run the TWIN A installer and select Tailscale, then choose 'Configure iPad Access' from the TWIN A tray icon.",
                "TWIN A — iPad Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await OpenDashboardWhenReadyAsync();
            return;
        }

        var online = await IsTailscaleOnlineAsync(tailscale);
        while (!online)
        {
            OpenTailscale();
            var choice = MessageBox.Show(
                "Tailscale is installed but is not connected yet.\n\n1. Sign in to Tailscale on this PC.\n2. Wait until Tailscale says Connected.\n3. Click Retry here.\n\nUse the same Tailscale account on the iPad.",
                "TWIN A — Connect Tailscale",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Retry)
            {
                await OpenDashboardWhenReadyAsync();
                return;
            }
            online = await IsTailscaleOnlineAsync(tailscale);
        }

        var serve = await RunProcessAsync(tailscale, "serve --bg 5055");
        var status = await RunProcessAsync(tailscale, "serve status");
        var combined = serve.Output + "\n" + status.Output;
        var match = Regex.Match(combined, @"https://[A-Za-z0-9.-]+\.ts\.net(?::\d+)?", RegexOptions.IgnoreCase);
        var url = match.Success ? match.Value : string.Empty;

        if (serve.ExitCode != 0 && !status.Output.Contains("127.0.0.1:5055", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Tailscale is connected, but TWIN A could not automatically configure Tailscale Serve.\n\nOpen the TWIN A README from the Start menu for the manual Tailscale Serve step.",
                "TWIN A — iPad Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            await OpenDashboardWhenReadyAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            try { Clipboard.SetText(url); } catch { }
            var open = MessageBox.Show(
                $"Private iPad access is ready.\n\nAddress:\n{url}\n\nThe address has been copied to the clipboard.\n\nOn the iPad:\n1. Install Tailscale.\n2. Sign in to the same Tailscale account.\n3. Open this address in Safari.\n4. Share → Add to Home Screen.\n\nOpen the private address on this PC now?",
                "TWIN A — iPad Access Ready",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
            }
        }
        else
        {
            MessageBox.Show(
                "Tailscale Serve is configured for TWIN A. Open the Tailscale Serve status from the README if you need to find the private .ts.net address.",
                "TWIN A — iPad Access Ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        await OpenDashboardWhenReadyAsync();
    }

    private static async Task<bool> IsTailscaleOnlineAsync(string tailscale)
    {
        var result = await RunProcessAsync(tailscale, "status --json");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) return false;
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            if (root.TryGetProperty("BackendState", out var backend) && backend.GetString()?.Equals("Running", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (root.TryGetProperty("Self", out var self) && self.TryGetProperty("Online", out var online) && online.ValueKind == JsonValueKind.True)
                return true;
        }
        catch { }
        return false;
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string executable, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await stdout) + "\n" + (await stderr);
            return (process.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
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
