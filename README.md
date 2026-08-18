# TWIN A Control Center

Turn an iPad into a private Windows control center.

TWIN A gives you live PC monitoring, OBS controls, Windows audio, Steam games, files, task/window management, workflows, and a high-refresh remote desktop from an iPad.

**You do not need coding experience to install the normal Windows version.**

---

## The easiest way to install TWIN A

### 1. Download the Windows installer

Open the **Releases** section of this GitHub repository and download the newest file named similar to:

```text
TwinA-Control-Center-Setup-0.9.0-win-x64.exe
```

Do not download the source-code ZIP unless you are a developer.

> Development/test builds may show an **Unknown publisher** Windows SmartScreen warning until release code-signing is added. Only download TWIN A from this repository's official Releases page.

### 2. Run the installer

Double-click the downloaded installer.

The installer installs TWIN A itself as a self-contained Windows application. Normal users do **not** need to separately install:

- .NET SDK
- Node.js
- npm
- Git
- Rider / Visual Studio

Those tools are only needed by developers who build TWIN A from source.

### 3. Choose the companion applications you want

During installation TWIN A asks which integrations should also be installed.

#### Tailscale — recommended

Tailscale provides the private connection between the Windows PC and the iPad.

Leave this selected if you want to use TWIN A from the iPad.

#### OBS Studio — optional

Select OBS Studio if you want:

- recording controls
- Replay Buffer controls
- OBS scenes
- OBS mixer controls
- recording workflows

If OBS is already installed, the installer leaves the existing installation in place.

#### Steam — optional

Select Steam if you want automatic Steam library/game discovery and game launching.

#### Discord — optional

Select Discord if you want TWIN A's Discord shortcuts/integration.

TWIN A uses Windows Package Manager (`winget`) to install selected third-party companion applications from their registered package sources. If Windows Package Manager is unavailable, TWIN A itself is still installed and the companion application can be installed manually later.

### 4. Choose whether TWIN A starts with Windows

The installer offers:

```text
Start TWIN A automatically when I sign in to Windows
```

This is recommended for an iPad control-panel setup.

TWIN A runs in your signed-in Windows session because features such as remote desktop, audio control and window control require access to your interactive desktop.

### 5. Finish installation

At the end of installation leave:

```text
Start TWIN A and configure private iPad access
```

selected.

TWIN A starts in the Windows notification area/system tray.

---

# First-time iPad setup

After installation TWIN A guides you through the Tailscale connection.

## On the Windows PC

If Tailscale is not signed in yet, TWIN A opens Tailscale and asks you to:

1. Sign in to Tailscale.
2. Wait until Tailscale says **Connected**.
3. Return to the TWIN A setup message and choose **Retry**.

TWIN A then configures private Tailscale Serve access to its local server on:

```text
127.0.0.1:5055
```

TWIN A remains bound to localhost. You should **not** port-forward port `5055` on your router.

When setup succeeds, TWIN A shows an address similar to:

```text
https://your-computer.your-tailnet.ts.net
```

The private address is copied to the Windows clipboard.

## On the iPad

1. Install **Tailscale** from the iPad App Store.
2. Sign in using the **same Tailscale account/tailnet** as the Windows PC.
3. Make sure Tailscale shows **Connected**.
4. Open **Safari**.
5. Open the private `.ts.net` address TWIN A gave you.
6. Confirm the TWIN A dashboard loads.

The PC can use Ethernet while the iPad uses Wi-Fi. They do not need to use the same physical network.

## Add TWIN A to the iPad Home Screen

In Safari:

1. Tap **Share**.
2. Tap **Add to Home Screen**.
3. If shown, enable **Open as Web App**.
4. Name it `TWIN A`.
5. Tap **Add**.

TWIN A can now be launched from the iPad Home Screen like an app.

TWIN A requests a Screen Wake Lock while the web app is visible so the iPad can remain useful as an always-on control panel. iPadOS may still release the lock in system-controlled situations such as power-saving conditions, and the physical power button always remains in control.

---

# Opening TWIN A later

After installation, TWIN A can be opened in several ways.

### Start menu

Open:

```text
Start → TWIN A Control Center
```

### Desktop shortcut

If you selected the desktop-shortcut option during installation, double-click:

```text
TWIN A Control Center
```

### System tray

TWIN A runs in the Windows notification area.

Right-click its tray icon for:

- **Open TWIN A**
- **Configure iPad Access**
- **Open Tailscale**
- **Restart TWIN A**
- **Exit TWIN A**

Double-clicking the tray icon opens the dashboard.

---

# If you skipped iPad setup

You can run it again at any time.

Open:

```text
Start → TWIN A - Configure iPad Access
```

or right-click the TWIN A tray icon and choose:

```text
Configure iPad Access
```

---

# Main features

## Home

The Home page provides quick access to the controls you use most often.

It can show real/live state for supported features instead of simply remembering the last button pressed.

Examples include:

- CPU usage
- RAM usage
- NVIDIA GPU usage and temperature when available
- OBS running state
- recording state
- Replay Buffer state
- Steam running state
- screenshots
- master audio controls

Where the target application does not provide trustworthy state readback, TWIN A does not pretend the action was verified.

## Studio / OBS

With OBS Studio configured, TWIN A can provide:

- start/stop recording
- pause/resume recording
- start/stop Replay Buffer
- save replay clips
- scene discovery and switching
- OBS audio-source discovery
- mute/unmute OBS audio sources
- recording/session markers

OBS WebSocket authentication is supported. Passwords are stored outside the Git repository.

## Audio

TWIN A supports Windows audio controls including:

- master volume
- master mute
- playback-device switching
- microphone/capture-device discovery
- OBS mixer controls
- **per-application Windows audio mixer**

The per-app mixer reads the actual Windows audio-session state and verifies volume/mute changes where Windows exposes readback.

## Games

TWIN A can:

- discover installed Steam libraries
- discover installed Steam games
- launch Steam games
- detect running games when possible
- store custom non-Steam games
- apply game profiles

A game profile can prepare OBS, Discord, audio output, volume, scenes, Replay Buffer and recording before launching a game.

## Desktop

The Desktop section contains several Windows-control tools.

### Window Manager

See real visible Windows applications and their state.

Supported actions include:

- focus
- minimize
- maximize
- restore
- close

### Task Manager

See running processes with information such as:

- process name
- PID
- memory usage
- window title
- responding state

TWIN A can end tasks and verifies that the process actually disappeared before reporting success.

Important/system/TWIN A processes are protected from the End Task button.

### App Mixer

Control volume and mute state for individual Windows audio sessions.

### Remote Screen

TWIN A can display the Windows desktop on the iPad with a high-refresh binary WebSocket stream.

It supports:

- all displays or one selected monitor
- measured FPS
- measured frame latency
- Max FPS / Balanced / High Quality modes
- full-screen iPad view
- Windows cursor rendering
- tap to click
- double-tap to double-click
- hold for right-click
- drag
- two-finger scroll
- pinch zoom
- keyboard shortcuts
- text input

Remote mouse/keyboard control is **OFF by default**. Screen viewing does not automatically enable remote input.

When remote input is enabled, TWIN A reports injected input as **EXECUTED • STATE UNVERIFIED** because Windows can confirm the input was sent but cannot prove what every third-party application did with that event.

### Pinch zoom

Use two fingers on the remote screen and move them apart/together.

Pinch zoom works even while the remote screen is in **VIEW ONLY** mode.

The on-screen zoom percentage / `−` / `+` / `FIT` overlay appears only while zoom is being adjusted, then disappears so it does not cover the Windows taskbar.

Use the **ZOOM** button in the Remote Screen header if you want to show the controls manually.

## Files

TWIN A includes a real file browser for ready fixed/removable Windows drives.

Supported operations include:

- browse
- open on the PC
- upload
- download
- create folder
- rename
- copy
- move
- delete

File operations are real. Treat destructive operations carefully.

## System

The System page can show:

- CPU/GPU/RAM information
- GPU temperature when supported
- primary physical network adapter
- network activity
- link speed
- Windows uptime
- operating-system details
- disk/free-space information

## Dev

The Dev section is intended for developers and can store project definitions, Git status, build/test/run commands and IDE shortcuts.

Normal TWIN A users do not need to install developer tools just to run the installed app.

## IoT

MQTT/IoT support is optional.

If no broker is configured, TWIN A should truthfully show that IoT is not configured rather than creating fake devices/data.

## Flows

Flows combine multiple actions into one workflow, for example:

```text
Open OBS
→ Start Replay Buffer
→ Set Windows volume
→ Open Discord
→ Launch a game
→ Start recording
```

---

# Understanding TWIN A status messages

TWIN A deliberately distinguishes between three outcomes.

## VERIFIED

The action completed and TWIN A confirmed the resulting state.

Examples can include:

- process ended and PID disappeared
- Windows audio mute state matched the requested value
- OBS reported recording active

## EXECUTED • STATE UNVERIFIED

TWIN A genuinely sent/executed the command, but the target application or Windows API does not provide trustworthy final-state readback for that specific operation.

This is not treated as VERIFIED.

## FAILED

The requested operation did not complete successfully.

---

# OBS setup

If you selected OBS during installation, install/configure OBS before expecting Studio controls to connect.

In OBS:

1. Open **Tools**.
2. Open **WebSocket Server Settings**.
3. Enable the WebSocket server.
4. Use port:

```text
4455
```

5. Enable authentication.
6. Choose a strong password.

TWIN A never needs that password committed to GitHub.

For source/developer setups the included password helper stores it as the Windows user environment variable:

```text
TWINA_OBS_PASSWORD
```

For installed/public builds, keep credentials local to the PC and never share them in GitHub issues/screenshots.

---

# Security and privacy

TWIN A controls real Windows functions, so its security model is intentionally conservative.

## Local server

The Control Server listens on:

```text
http://127.0.0.1:5055
```

Do not expose that port with router port forwarding.

## Private iPad access

The intended remote path is:

```text
iPad
  ↓
Tailscale
  ↓
private .ts.net address
  ↓
Tailscale Serve
  ↓
127.0.0.1:5055
```

## Remote input

Remote mouse/keyboard input is disabled by default and must be explicitly enabled by the user.

## Personal data

The public source repository must not contain a user's:

- Windows username/path
- public IP address
- local IP address
- Tailscale private hostname
- OBS password
- MQTT password
- API keys/tokens
- private configuration file

Machine-specific TWIN A configuration is stored outside the installation/source tree at:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

Secrets such as OBS/MQTT passwords are also kept outside source control.

---

# Updating TWIN A

For normal users, download and run the newer installer from **GitHub Releases**.

The installer uses the same application ID so it can upgrade the existing TWIN A installation rather than requiring users to rebuild the program from source.

Your machine-specific configuration under `%LOCALAPPDATA%\TwinAControlCenter` is separate from the application installation.

Before major upgrades, backing up that folder is still recommended.

---

# Uninstalling TWIN A

Open:

```text
Windows Settings → Apps → Installed apps
```

Find:

```text
TWIN A Control Center
```

and choose **Uninstall**.

TWIN A stops its launcher, Control Server and Desktop Agent during uninstall.

Companion applications such as Tailscale, OBS Studio, Steam and Discord are **not automatically removed** when TWIN A is uninstalled. They are independent third-party applications and may contain their own accounts/settings or be used for other purposes.

---

# Troubleshooting

## TWIN A is installed but the iPad cannot connect

On the PC:

1. Make sure TWIN A is running in the system tray.
2. Make sure Tailscale is connected.
3. Right-click the TWIN A tray icon.
4. Choose **Configure iPad Access**.

On the iPad:

1. Make sure Tailscale is installed.
2. Make sure it is signed into the same tailnet/account.
3. Use the private `.ts.net` address — not `127.0.0.1`.

## Why `127.0.0.1` does not work on the iPad

`127.0.0.1` always means "this device".

On the iPad it points to the iPad itself, not the Windows PC.

Use the Tailscale `.ts.net` address.

## OBS says offline

Check that:

- OBS is running
- WebSocket server is enabled
- WebSocket port is `4455`
- authentication settings match TWIN A

## Replay Buffer fails

In OBS enable Replay Buffer under the appropriate Output settings first.

## Steam games are missing

Open Steam once and allow it to finish normal setup, then restart/rescan TWIN A.

## Remote Screen works but mouse/keyboard does not

This is expected when the Remote Screen says:

```text
VIEW ONLY
```

Enable **CONTROL ENABLED** only when you intentionally want remote input.

## Pinch zoom does not control Windows

Pinch zoom changes the iPad view only. It does not zoom Windows itself.

Two-finger scrolling sends Windows mouse-wheel input only when remote control is enabled.

## Windows Package Manager is unavailable

TWIN A itself remains installed.

Install the selected companion app manually and restart TWIN A.

---

# Developer / source installation

This section is only for people modifying TWIN A itself.

Normal users should use the installer above.

## Requirements

- Windows 11 recommended
- Git
- .NET 10 SDK
- Node.js 24
- npm
- optional Rider / Visual Studio
- Inno Setup 7 or 6 only if building the Windows installer locally

## Clone

```powershell
git clone https://github.com/AhmadAmerBakran/TwinAControlCenter.git
cd .\TwinAControlCenter
```

## Build source

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## Run source build

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

## Smoke test

In another PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

## Build the Windows installer locally

Install Inno Setup, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-installer.ps1
```

The generated setup executable is written under:

```text
artifacts\installer\
```

The installer payload publishes the Control Server, Desktop Agent and Launcher as **self-contained Windows x64 applications**, so end users do not need the .NET SDK/runtime or Node.js just to run TWIN A.

---

# Repository structure

```text
TwinAControlCenter
│
├── backend
│   ├── TwinA.ControlServer
│   ├── TwinA.DesktopAgent
│   └── TwinA.Launcher
│
├── frontend
│   └── Angular PWA
│
├── installer
│   ├── TwinAControlCenter.iss
│   └── install-dependencies.ps1
│
├── scripts
│   ├── build.ps1
│   ├── run.ps1
│   ├── smoke-test.ps1
│   └── package-installer.ps1
│
├── docs
├── README.md
└── TwinAControlCenter.sln
```

---

# Important final note

TWIN A is intended to be a **private personal Windows control surface**, not a publicly exposed remote-administration server.

For the intended setup:

- keep the Control Server on localhost
- use Tailscale for private device connectivity
- do not router-port-forward TWIN A
- keep passwords and personal configuration out of GitHub
- leave remote input disabled unless you need it
- pay attention to VERIFIED vs EXECUTED / STATE UNVERIFIED results
