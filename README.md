# TWIN A Control Center

Turn an iPad into a private control center for a Windows PC.

TWIN A combines live system monitoring, OBS controls, Windows audio, Steam launching, files, desktop management, workflows, optional MQTT devices, and a remote screen in one touch-friendly interface.

> Normal users should install TWIN A from **GitHub Releases**. You do not need .NET, Node.js, Git, Rider, or Visual Studio to run the installed app.

## Quick install

1. Open this repository's **Releases** page.
2. Download the newest `TwinA-Control-Center-Setup-...-win-x64.exe`.
3. Run the installer.
4. Keep **Tailscale** selected if you want private iPad access.
5. Optionally install OBS Studio, Steam, and Discord when you use those integrations.
6. Leave **Start TWIN A automatically when I sign in to Windows** enabled for an always-ready control panel.
7. Finish setup and follow the iPad-access prompt.

Development builds may show a Windows SmartScreen **Unknown publisher** warning until release code-signing is added. Download installers only from this repository's official Releases page.

## Connect the iPad

TWIN A keeps its web server on `127.0.0.1:5055` and uses **Tailscale Serve** for private access. Do not port-forward port `5055` on your router.

On the PC:

1. Sign in to Tailscale.
2. Right-click the **TWIN A** tray icon.
3. Choose **Configure iPad Access**.
4. TWIN A gives you a private `.ts.net` address and copies it to the clipboard.

On the iPad:

1. Install Tailscale and sign in to the same tailnet/account.
2. Open the private `.ts.net` address in Safari.
3. Use **Share → Add to Home Screen** if you want TWIN A to launch like an app.

The PC and iPad do not have to be on the same physical network as long as both can reach the same Tailscale network.

## What TWIN A can control

- **Home:** CPU, GPU, RAM, GPU temperature, recording, replay, Steam, Discord and quick actions.
- **Studio:** OBS recording, pause/resume, Replay Buffer, scenes, OBS audio sources and session markers.
- **Audio:** Windows master volume, audio devices, per-app mixer, Discord voice shortcuts and a local soundboard.
- **Games:** Steam discovery, launching, custom games and game automation profiles.
- **System:** live hardware/network information, screenshots, Task Manager, lock/sleep/restart/shutdown actions.
- **Desktop:** window management, process control, app audio, remote screen and remote mouse/keyboard input.
- **Files:** browse, upload, download, open, rename, copy, move and delete files with protected system paths.
- **Dev:** saved development projects plus build/test/run/IDE shortcuts.
- **IoT:** optional MQTT devices with state-topic verification when available.
- **Flows:** multi-step automation for common gaming, streaming and desktop routines.

## Status messages

TWIN A tries not to pretend that an action succeeded when it cannot prove the result.

- **VERIFIED** — the resulting state was read back successfully.
- **EXECUTED • STATE UNVERIFIED** — the command was genuinely sent, but Windows or the target app does not expose trustworthy final-state readback.
- **FAILED** — the requested action did not complete.

Discord mute/deafen/soundboard commands are routed to the Discord desktop window and then return focus to the window you were using. Discord does not expose a supported API for TWIN A to verify the final self-mute/deafen state, so those actions remain state-unverified.

## OBS setup

In OBS open **Tools → WebSocket Server Settings** and:

1. Enable the WebSocket server.
2. Use port `4455`.
3. Enable authentication.
4. Set a strong password.

The tray menu includes **Configure OBS Password**. Secrets stay local to the PC and must never be committed to the repository.

Replay controls also require Replay Buffer to be enabled in OBS Output settings.

## Settings and local data

The Settings page contains the controls that affect safety and remote access. Machine-specific configuration is stored outside the application/source folder at:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

Remote mouse/keyboard control is **off by default**. System-path protection and power-action confirmation are enabled by default.

Do not put passwords, private Tailscale hostnames, local/public IP addresses, API tokens, or personal Windows paths in GitHub issues or commits.

## Help and troubleshooting

The built-in **Help Center** is available from the in-app Help button, the Start menu, and the TWIN A tray menu.

Common checks:

- **iPad cannot connect:** confirm TWIN A and Tailscale are running, then use **Configure iPad Access** again. The iPad must use the private `.ts.net` address, not `127.0.0.1`.
- **OBS is offline:** start OBS, enable its WebSocket server on port `4455`, and confirm the saved password.
- **Steam games are missing:** open Steam once, let it finish setup, then rescan/restart TWIN A.
- **Remote screen works but input does not:** enable remote control intentionally; view-only mode does not inject mouse or keyboard input.
- **Discord shortcuts do nothing:** make sure the desktop Discord client is running and has a usable main window.

## Updates

TWIN A includes an updater for published GitHub Releases. Right-click the tray icon and choose **Check for Updates**. The updater verifies the downloaded installer before starting the upgrade.

Your local configuration under `%LOCALAPPDATA%\TwinAControlCenter` is separate from the installed application files.

## Uninstall

Use **Windows Settings → Apps → Installed apps → TWIN A Control Center → Uninstall**.

Tailscale, OBS Studio, Steam and Discord are separate applications and are not removed automatically.

## Developers

### Requirements

- Windows 11 recommended
- Git
- .NET 10 SDK
- Node.js 24 + npm
- optional Rider / Visual Studio
- Inno Setup 6 or 7 only when building the Windows installer locally

### Clone, build and run

```powershell
git clone https://github.com/AhmadAmerBakran/TwinAControlCenter.git
cd .\TwinAControlCenter
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Run the smoke test in another PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

Build the installer with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-installer.ps1
```

Installer output is written under `artifacts\installer\`.

### Repository layout

```text
backend/     Control Server, Desktop Agent, Launcher
frontend/    Angular touch-first UI / PWA
installer/   Inno Setup definition and Windows branding
scripts/     build, run, smoke-test and packaging scripts
.github/     CI workflows
```

## Security model

TWIN A controls real Windows functions, so the intended design is conservative: localhost-only server, private Tailscale access, remote input disabled by default, protected destructive file operations, and local-only secrets/configuration.

Please review code and release notes before installing software that can control your PC.