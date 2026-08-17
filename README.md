# TWIN A Control Center

> Turn an iPad into a private control center for a Windows PC.

TWIN A Control Center is a Windows + iPad control dashboard for OBS, audio, Steam games, files, system information, developer workflows, and optional MQTT/IoT devices. The Control Server stays on `127.0.0.1` and the iPad reaches it privately through Tailscale Serve.

This guide is written so a user can start from a clean supported Windows computer and follow the steps in order without editing source code or hardcoded machine paths.

## Supported environment

TWIN A is currently intended for:

- Windows 10/11 x64, with Windows 11 recommended
- .NET 10 SDK
- Node.js 24
- Git for Windows
- Tailscale for Windows
- iPad/iPadOS with Safari

Optional integrations:

- OBS Studio
- Steam
- Discord Desktop
- JetBrains Rider
- NVIDIA GPU telemetry through `nvidia-smi`
- MQTT broker/devices

TWIN A is **not** currently a macOS or Linux host application because the Desktop Agent uses Windows APIs.

## What it can do

Depending on the software installed on the PC, TWIN A can provide live CPU/RAM/GPU information, Windows volume and audio-device control, OBS recording/replay/scene controls, Steam game discovery and launching, Discord shortcuts, screenshots, file management, developer-project controls, workflows, and optional MQTT/IoT controls.

TWIN A reports actions as verified, executed-but-unverified, or failed instead of claiming success when the final state cannot be proven.

## How it works

```text
                    iPad / Safari PWA
                           │
                           │ private HTTPS
                           ▼
                        Tailscale
                           │
                           ▼
                     Windows PC
                           │
                    Tailscale Serve
                           │
                           ▼
                   127.0.0.1:5055
                           │
                  TWIN A Control Server
                     │            │
                    OBS      Desktop Agent
                                  │
                       Windows / Audio / Files
```

The PC may use Ethernet while the iPad uses Wi-Fi. They do not need to be on the same physical network as long as both devices are connected to the same Tailscale tailnet.

**Do not port-forward port `5055` on your router.**

# First-time installation

Follow these steps in order.

## 1. Install Git for Windows

Install Git from the official Git for Windows website, then open a new PowerShell window and verify:

```powershell
git --version
```

## 2. Install the .NET 10 SDK

Install the **.NET 10 SDK**, not only the runtime.

Verify:

```powershell
dotnet --version
```

The version must begin with `10.`.

## 3. Install Node.js 24

Install Node.js 24, then reopen PowerShell and verify:

```powershell
node --version
npm --version
```

## 4. Install Tailscale

Install Tailscale for Windows, sign in, and connect the PC to your tailnet.

Verify:

```powershell
tailscale status
```

If the command is not on PATH, use:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```

You do not need an Exit Node for TWIN A.

## 5. Install optional applications

Install only what you want to use:

- OBS Studio for recording/scene/replay controls
- Steam for automatic Steam library discovery
- Discord Desktop for Discord shortcuts
- Rider if you want IDE integration

OBS may be installed normally or through Steam. TWIN A automatically looks for common OBS installations and Steam library locations. A custom/portable OBS path can also be configured without changing source code.

# Download TWIN A

Choose a project folder. This example uses `RiderProjects`, but the repository does not depend on that folder name.

```powershell
New-Item -ItemType Directory -Force "$HOME\RiderProjects" | Out-Null
Set-Location "$HOME\RiderProjects"
git clone https://github.com/AhmadAmerBakran/TwinAControlCenter.git
Set-Location ".\TwinAControlCenter"
```

Verify that you are in the repository root:

```powershell
Test-Path ".\TwinAControlCenter.sln"
Test-Path ".\frontend\package.json"
Test-Path ".\backend\TwinA.ControlServer\TwinA.ControlServer.csproj"
```

All three should return `True`.

# Build TWIN A

From the repository root run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The build script now:

1. checks that .NET, Node.js and npm are available
2. verifies that the .NET 10 SDK is being used
3. detects machine-specific optional application paths
4. installs the exact frontend dependency set from `package-lock.json` with `npm ci`
5. builds the Angular production frontend
6. copies the frontend into the ASP.NET server
7. builds the Control Server
8. builds the Windows Desktop Agent

A successful build ends with:

```text
TWIN A build complete - all build steps succeeded.
```

Do not continue if the build reports an error. See Troubleshooting below.

# OBS setup (optional)

Skip this section if you do not use OBS.

## Enable OBS WebSocket

Open OBS and go to:

```text
Tools → WebSocket Server Settings
```

Use:

- WebSocket enabled
- port `4455`
- authentication enabled
- a strong password

Do not place the password in GitHub, `README.md`, or `appsettings.json`.

Save it for your Windows user with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
```

The password is stored in the Windows user environment as `TWINA_OBS_PASSWORD`.

## OBS application path

TWIN A runs `scripts\detect-apps.ps1` automatically during build/startup. It checks normal OBS installation locations and Steam libraries.

You can run detection manually:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\detect-apps.ps1
```

For a portable or unusual OBS installation, set the path once:

```powershell
[Environment]::SetEnvironmentVariable(
    "TWINA_OBS_PATH",
    "C:\Your\Custom\OBS\bin\64bit\obs64.exe",
    "User"
)
```

Restart TWIN A afterward.

If you use Replay Buffer, enable it in OBS under `Settings → Output → Replay Buffer`.

# Run TWIN A on the PC

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

The script starts two components:

- **Desktop Agent** in a separate PowerShell window
- **Control Server** in the original PowerShell window

Keep both running.

The server listens only on:

```text
http://127.0.0.1:5055
```

If you receive a message saying TWIN A has not been built, run `scripts\build.ps1` first.

# Test the local installation

Before configuring the iPad, test on the PC.

Open:

```text
http://127.0.0.1:5055
```

Then check:

```text
http://127.0.0.1:5055/api/health
```

Run the read-only smoke test in a second PowerShell window:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

A healthy installation should show PASS results for health, live state, system details, audio devices, game discovery, drives, developer projects, flows, settings and IoT state. OBS warnings are normal when OBS is intentionally closed.

# Connect the iPad with Tailscale

Install the official Tailscale app on the iPad and sign in to the **same tailnet** as the PC. Allow the VPN configuration when iPadOS asks.

Confirm Tailscale shows connected on both devices.

# Publish TWIN A privately with Tailscale Serve

TWIN A must already be running locally.

On Windows, open **PowerShell as Administrator**.

Go to the repository and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\tailscale-serve.ps1
```

The script checks for administrator rights, finds the Tailscale CLI, creates a background Serve configuration for local port `5055`, and prints the current Serve status.

Tailscale should provide a private HTTPS address similar to:

```text
https://your-pc.your-tailnet.ts.net
```

Do not open `127.0.0.1:5055` on the iPad; on the iPad that address means the iPad itself.

Do not use Tailscale Funnel and do not configure router port forwarding for TWIN A.

# Open TWIN A on the iPad

Make sure:

- Control Server is running
- Desktop Agent is running
- Tailscale is connected on the PC
- Tailscale is connected on the iPad
- Tailscale Serve is configured

Open the `.ts.net` HTTPS address in Safari.

When everything works, use Safari's **Share → Add to Home Screen** and name it `TWIN A`.

# First-use checklist

Before relying on the dashboard, test the functions you intend to use:

- CPU/RAM/GPU values update
- Windows volume and mute work
- screenshot creates a real file
- audio devices are listed
- Steam games appear and a game can launch
- OBS state is correct when OBS is open
- OBS record/replay/scene controls work
- drives and folders appear in Files
- create/rename/delete only harmless test files first
- Dev projects report correct paths/tools
- IoT shows NOT CONFIGURED if MQTT is not configured

File operations affect real Windows files. Be careful with delete, move and rename.

# Normal daily use

On the PC:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Normally Tailscale Serve persists after it has been configured once. Check it with an Administrator PowerShell if needed:

```powershell
tailscale serve status
```

On the iPad, connect Tailscale and open the TWIN A Home Screen app.

# Updating TWIN A

Stop TWIN A first, then:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
git pull
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Run the smoke test again after an update.

Because the build uses `npm ci`, frontend dependencies are recreated from the committed lock file rather than relying on whatever happened to be installed previously.

# Personal configuration and backups

Machine-specific TWIN A configuration is stored outside the Git repository at:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

It can contain custom games, profiles, developer projects, flows, MQTT configuration and UI preferences.

Secrets and machine paths are also kept outside Git:

```text
TWINA_OBS_PASSWORD
TWINA_MQTT_PASSWORD
TWINA_OBS_PATH
```

Back up `config.json` before reinstalling Windows if you want to keep your custom setup.

Example:

```powershell
New-Item -ItemType Directory -Force "$HOME\Documents\TwinABackup" | Out-Null
Copy-Item "$env:LOCALAPPDATA\TwinAControlCenter\config.json" "$HOME\Documents\TwinABackup\config.json" -Force
```

# Restoring after reinstalling Windows

1. Install Git, .NET 10 SDK, Node.js 24 and Tailscale.
2. Install optional applications you use.
3. Clone the repository again with the real GitHub URL above.
4. Restore `config.json` to `%LOCALAPPDATA%\TwinAControlCenter\config.json` if you have a backup.
5. Run `scripts\set-obs-password.ps1` again if you use OBS.
6. Run `scripts\set-mqtt-password.ps1` again if you use authenticated MQTT.
7. Run `scripts\build.ps1`.
8. Run `scripts\run.ps1`.
9. Sign in to Tailscale on PC/iPad.
10. Run `scripts\tailscale-serve.ps1` from Administrator PowerShell.
11. Run the smoke test.
12. Open the new `.ts.net` URL on the iPad.

A Windows reinstall can create a new Tailscale device identity/hostname, so do not assume the old `.ts.net` address will always remain identical.

# Optional automatic startup

Only configure automatic startup after manual startup works correctly.

Use Windows Task Scheduler with:

- trigger: **At log on**
- run only when your user is logged on
- program: `powershell.exe`
- arguments:

```text
-ExecutionPolicy Bypass -File "%USERPROFILE%\RiderProjects\TwinAControlCenter\scripts\run.ps1"
```

If you cloned the repository somewhere else, use that real path instead.

The Desktop Agent should run in the logged-in interactive user session rather than as a hidden Windows system service.

# Security

TWIN A controls real Windows actions and files.

Use the intended security model:

```text
iPad → Tailscale → private .ts.net URL → Tailscale Serve → 127.0.0.1:5055
```

Important rules:

- never port-forward `5055`
- do not expose the Control Server directly to the public internet
- do not commit OBS/MQTT passwords or API secrets
- protect your Tailscale account with strong authentication
- only allow trusted devices/users into your tailnet
- treat Files operations as real file operations
- leave system-path protection enabled unless you understand the consequences

# Troubleshooting

## `git`, `dotnet`, `node` or `npm` is not recognized

Install the missing prerequisite, close PowerShell, open a new window and retry.

## Build says .NET 10 is required

Check:

```powershell
dotnet --version
```

Install the .NET 10 SDK if the active version does not begin with `10.`.

## `npm ci` fails

Make sure you are using a supported Node.js installation and have internet access for the first dependency download. Do not manually delete or edit `package-lock.json` to work around an error.

## OBS controls work but Launch OBS fails

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\detect-apps.ps1
```

If OBS is portable/custom, set `TWINA_OBS_PATH` manually as shown in the OBS section.

## OBS shows offline

Check that OBS is open, WebSocket is enabled on port `4455`, authentication is enabled, and the saved password is correct. Run `set-obs-password.ps1` again and restart TWIN A if needed.

## TWIN A works on the PC but not the iPad

Check:

```powershell
tailscale status
tailscale serve status
```

Also confirm the iPad is connected to the same tailnet and that Safari uses the private `.ts.net` HTTPS address.

## Tailscale Serve script says Administrator is required

Close that PowerShell window, right-click PowerShell, choose **Run as administrator**, return to the repository and run `scripts\tailscale-serve.ps1` again.

## Desktop Agent is offline

Stop both TWIN A windows and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Keep the separate Desktop Agent window open.

## Port 5055 is already in use

Check:

```powershell
Get-NetTCPConnection -LocalPort 5055 -ErrorAction SilentlyContinue
```

An older TWIN A Control Server may still be running.

## GPU telemetry is unavailable

Current GPU usage/temperature support is primarily NVIDIA-based. Test:

```powershell
nvidia-smi --query-gpu=name,utilization.gpu,temperature.gpu --format=csv,noheader
```

The rest of TWIN A can still work without NVIDIA telemetry.

# Repository structure

```text
TwinAControlCenter
│
├── backend
│   ├── TwinA.ControlServer
│   └── TwinA.DesktopAgent
│
├── frontend
│   └── Angular PWA
│
├── scripts
│   ├── build.ps1
│   ├── detect-apps.ps1
│   ├── run.ps1
│   ├── smoke-test.ps1
│   ├── tailscale-serve.ps1
│   ├── set-obs-password.ps1
│   └── set-mqtt-password.ps1
│
├── docs
├── README.md
└── TwinAControlCenter.sln
```

# Quick install summary

For experienced users:

```powershell
git clone https://github.com/AhmadAmerBakran/TwinAControlCenter.git
cd TwinAControlCenter
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
# optional OBS password setup:
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Then run the smoke test in another terminal and configure Tailscale Serve from **Administrator PowerShell**:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\tailscale-serve.ps1
```

Open the resulting private `.ts.net` URL on the iPad and add it to the Home Screen.
