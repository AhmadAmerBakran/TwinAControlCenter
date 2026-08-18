# TWIN A Control Center

**Your Windows PC, comfortably controlled from an iPad.**

TWIN A is a private control dashboard for OBS, Windows audio, games, files, desktop control and reusable Flows. It runs on your PC and is designed to be opened from an iPad through Tailscale.

## Install in a few minutes

1. Open **Releases** and download the newest `TwinA-Control-Center-Setup-...-win-x64.exe`.
2. Run the installer on your Windows PC.
3. Keep **Tailscale** selected when the installer asks about companion apps.
4. Sign in to Tailscale on the PC.
5. On the iPad, install Tailscale, sign in to the same account, then open the private `.ts.net` address TWIN A gives you.

That is it. Normal users do **not** need .NET, Node.js, Visual Studio or other developer tools.

## What can TWIN A do?

- Control OBS recording, Replay Buffer, scenes and audio sources.
- Control Windows volume, audio devices and per-app audio.
- Discover and launch Steam games.
- Browse files and control Windows apps/tasks.
- View and control the Windows desktop from the iPad.
- Build **Flows** from ready-made actions instead of memorising commands.
- Keep machine-specific settings private on your own PC.

Remote mouse and keyboard control is **off by default**.

## Quick tips

- Right-click the TWIN A tray icon for **Open**, **Help**, **Check for Updates**, iPad setup and restart.
- In Safari, use **Share → Add to Home Screen** for an app-like iPad experience.
- Use **Settings** for safety and display preferences.
- Use the **Help** button in TWIN A whenever you need the full guide.

TWIN A checks published GitHub Releases for updates and verifies the downloaded Windows installer before it runs.

## If something does not work

**iPad cannot connect:** make sure Tailscale is connected on both devices and both use the same tailnet. Do not use `127.0.0.1` on the iPad.

**OBS is offline:** start OBS and enable its WebSocket server on port `4455`.

**Remote screen works but input does not:** remote control is intentionally disabled until you enable it.

For more details, open the built-in **Help Center**.

## Developers

Source builds need Windows, .NET 10, Node.js 24 and npm:

```powershell
git clone https://github.com/AhmadAmerBakran/TwinAControlCenter.git
cd .\TwinAControlCenter
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Normal users should use the installer from **Releases** instead.

## Privacy

TWIN A binds its Control Server to `127.0.0.1` and is intended to be reached privately through Tailscale. Do not port-forward port `5055`.

Machine-specific configuration stays outside the repository under `%LOCALAPPDATA%\TwinAControlCenter`.

See [LICENSE](LICENSE) for license terms.
