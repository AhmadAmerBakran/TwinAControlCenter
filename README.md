# TWIN A Control Center

> Turn an iPad into a private, modern command center for your Windows PC.

TWIN A Control Center is designed so that you can control and monitor your Windows computer from an iPad without exposing the control server directly to the public internet.

You do **not** need to know programming to install and use it. This guide walks through the complete setup from an empty Windows PC to a working TWIN A app on your iPad.

---

## Table of contents

1. [What TWIN A can do](#what-twin-a-can-do)
2. [How it works](#how-it-works)
3. [Before you start](#before-you-start)
4. [Step 1 — Install the required software](#step-1--install-the-required-software)
5. [Step 2 — Download TWIN A from GitHub](#step-2--download-twin-a-from-github)
6. [Step 3 — Build TWIN A](#step-3--build-twin-a)
7. [Step 4 — Set up OBS Studio](#step-4--set-up-obs-studio)
8. [Step 5 — Run TWIN A on the PC](#step-5--run-twin-a-on-the-pc)
9. [Step 6 — Test the installation](#step-6--test-the-installation)
10. [Step 7 — Connect the PC to Tailscale](#step-7--connect-the-pc-to-tailscale)
11. [Step 8 — Connect the iPad to Tailscale](#step-8--connect-the-ipad-to-tailscale)
12. [Step 9 — Publish TWIN A privately with Tailscale Serve](#step-9--publish-twin-a-privately-with-tailscale-serve)
13. [Step 10 — Open TWIN A on the iPad](#step-10--open-twin-a-on-the-ipad)
14. [Step 11 — Install TWIN A on the iPad Home Screen](#step-11--install-twin-a-on-the-ipad-home-screen)
15. [First-use checklist](#first-use-checklist)
16. [What each tab does](#what-each-tab-does)
17. [Normal daily use](#normal-daily-use)
18. [Stopping TWIN A](#stopping-twin-a)
19. [Updating TWIN A from GitHub](#updating-twin-a-from-github)
20. [Backing up your personal TWIN A configuration](#backing-up-your-personal-twin-a-configuration)
21. [Restoring TWIN A after formatting Windows](#restoring-twin-a-after-formatting-windows)
22. [Optional — Start TWIN A automatically when you sign in to Windows](#optional--start-twin-a-automatically-when-you-sign-in-to-windows)
23. [Security](#security)
24. [Troubleshooting](#troubleshooting)
25. [Advanced and optional features](#advanced-and-optional-features)
26. [Project folders](#project-folders)

---

# What TWIN A can do

TWIN A combines several types of PC control into one iPad interface.

Depending on the applications installed on your PC, it can provide:

- live CPU usage
- live RAM usage
- NVIDIA GPU usage
- NVIDIA GPU temperature
- Windows master volume
- mute/unmute
- Windows audio-device discovery
- audio output switching
- OBS Studio connection status
- start/stop OBS recording
- pause/resume OBS recording
- Replay Buffer start/stop
- save Replay Buffer clips
- dynamic OBS scene switching
- OBS audio-input mute controls
- recording markers
- screenshots
- open screenshot folder
- Steam library discovery
- launch installed Steam games
- custom game profiles
- Steam Library shortcut
- Discord mute/deafen controls
- Discord Soundboard shortcut
- Windows network download/upload activity
- network-adapter information
- disk information
- PC uptime
- file browsing
- file upload/download
- create/rename/copy/move/delete files
- developer-project controls
- Git status
- build/test/run commands
- workflows
- optional MQTT/IoT devices

TWIN A follows one important rule:

> **It should not claim that something succeeded when it cannot prove that it happened.**

The interface can therefore report three different result levels:

- **VERIFIED** — the result was checked after the command.
- **EXECUTED / STATE UNVERIFIED** — the command was genuinely sent, but the target program does not provide a reliable way to confirm the final state.
- **FAILED** — the command did not complete successfully.

---

# How it works

TWIN A is made of three main parts.

```text
                         iPad
                  TWIN A web app
                         │
                         │ private HTTPS
                         ▼
                     Tailscale
                         │
                         ▼
                  Windows computer
                         │
                Tailscale Serve
                         │
                         ▼
                  127.0.0.1:5055
                         │
                TWIN A Control Server
                   │             │
                   │             │
                 OBS       Desktop Agent
                                 │
                         Windows / Steam /
                         Audio / Files / etc.
```

The PC can stay connected through **Ethernet**. It does not need to use Wi-Fi.

The iPad can be on Wi-Fi while the PC is on Ethernet. Tailscale creates the private connection between them.

TWIN A's ASP.NET server listens only on:

```text
http://127.0.0.1:5055
```

That address is local to the PC. Do **not** port-forward port `5055` on your router.

---

# Before you start

## Supported / recommended environment

For the tested setup, use:

- **Windows 11**
- **.NET 10 SDK**
- **Node.js 24**
- **Git for Windows**
- **Tailscale**
- **iPad with Safari**
- **OBS Studio** if you want OBS controls
- **Steam** if you want automatic Steam game discovery
- **Discord Desktop** if you want Discord shortcuts

Optional:

- **JetBrains Rider** if you want to edit the project or use Rider from the Dev tab
- an NVIDIA GPU for the current NVIDIA GPU usage/temperature monitoring
- an MQTT broker if you want IoT features

You do **not** need:

- Xcode
- a Mac
- an App Store developer account
- a public web server
- port forwarding
- Docker
- a Wi-Fi connection on the PC

---

# Step 1 — Install the required software

Do this only once on a new Windows installation.

## 1. Install Git for Windows

Download Git for Windows from:

[Git for Windows](https://git-scm.com/download/win)

Install it using the normal/default options.

After installation, open **Windows PowerShell** and run:

```powershell
git --version
```

You should see something similar to:

```text
git version 2.x.x.windows.x
```

If Windows says `git` is not recognized, close PowerShell, open it again, and retry.

---

## 2. Install .NET 10 SDK

Download the **.NET 10 SDK**, not only the runtime:

[Download .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

For a normal modern Windows PC, use the **Windows x64 SDK**.

After installation, close and reopen PowerShell.

Run:

```powershell
dotnet --version
```

You should see a version beginning with:

```text
10.
```

For example:

```text
10.0.302
```

---

## 3. Install Node.js

Download Node.js from:

[Node.js Downloads](https://nodejs.org/en/download)

Node.js 24 is recommended for the tested TWIN A setup.

After installation, close and reopen PowerShell.

Check:

```powershell
node --version
npm --version
```

You should receive version numbers for both commands.

Example:

```text
v24.x.x
12.x.x
```

---

## 4. Install Tailscale on Windows

Download and install Tailscale:

[Tailscale for Windows](https://tailscale.com/download/windows)

After installation:

1. Look for the Tailscale icon in the Windows system tray.
2. Open it.
3. Choose **Log in**.
4. Sign in with the account you want to use for your private Tailscale network.
5. Complete the authorization in the browser.

You do **not** need to configure an Exit Node for TWIN A.

Leave:

```text
Exit node: None
```

That is normal.

Check the connection in PowerShell:

```powershell
tailscale status
```

If PowerShell cannot find the `tailscale` command, use:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```

---

## 5. Install OBS Studio — optional but recommended

If you want recording, scene, Replay Buffer and OBS audio controls, install OBS Studio:

[OBS Studio](https://obsproject.com/download)

Modern OBS Studio versions include OBS WebSocket support, so a separate obs-websocket plugin is normally not required.

You will configure OBS later in this guide.

---

## 6. Install Steam — optional

If Steam is installed, TWIN A can automatically discover installed Steam libraries and games.

Install Steam normally and sign in.

You do not need to manually add every Steam game to TWIN A.

---

## 7. Install Discord Desktop — optional

Install the normal Discord desktop application if you want Discord mute/deafen and Soundboard shortcuts.

TWIN A does not require external virtual-audio tools for its normal Discord controls.

---

# Step 2 — Download TWIN A from GitHub

You do not need to download ZIP files manually.

## 1. Open the TWIN A GitHub repository

On the repository page:

1. Click the green **Code** button.
2. Select **HTTPS**.
3. Click the copy button beside the repository address.

The address will look similar to:

```text
https://github.com/OWNER/TwinAControlCenter.git
```

Do not type `OWNER` literally. Copy the real URL from GitHub.

---

## 2. Create a place for the project

Open **Windows PowerShell**.

Run:

```powershell
New-Item -ItemType Directory -Force "$HOME\RiderProjects" | Out-Null
```

Then:

```powershell
Set-Location "$HOME\RiderProjects"
```

---

## 3. Clone the repository

Use the URL you copied from GitHub:

```powershell
git clone https://github.com/OWNER/TwinAControlCenter.git
```

After cloning, enter the project folder:

```powershell
Set-Location ".\TwinAControlCenter"
```

To verify that you are in the correct folder:

```powershell
Get-Location
```

You should be in a path similar to:

```text
C:\Users\YourName\RiderProjects\TwinAControlCenter
```

Now verify the important files:

```powershell
Test-Path ".\TwinAControlCenter.sln"
Test-Path ".\frontend\package.json"
Test-Path ".\backend\TwinA.ControlServer\TwinA.ControlServer.csproj"
```

All three should return:

```text
True
```

---

# Step 3 — Build TWIN A

Stay in the root TWIN A folder:

```text
...\RiderProjects\TwinAControlCenter
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## What the build script does

The script automatically:

1. checks the Angular frontend
2. installs npm dependencies on the first build if needed
3. builds the Angular production app
4. copies the frontend into the ASP.NET server
5. builds the Control Server
6. builds the Windows Desktop Agent

The first build can take longer because npm packages may need to be downloaded.

A successful build ends with:

```text
TWIN A build complete - all build steps succeeded.
```

Do not continue if the build reports a red error.

See [Troubleshooting](#troubleshooting) if the build fails.

---

# Step 4 — Set up OBS Studio

Skip this section if you do not use OBS.

## 1. Open OBS Studio

Start OBS normally.

---

## 2. Enable OBS WebSocket

In OBS:

1. Open **Tools**.
2. Open **WebSocket Server Settings** / **obs-websocket Settings**.
3. Enable the WebSocket server.
4. Use port:

```text
4455
```

5. Enable authentication.
6. Set a strong password.
7. Click **Apply** / **OK**.

Do **not** put this password into source code, `README.md`, `appsettings.json`, or GitHub.

---

## 3. Save the OBS password securely for TWIN A

Back in PowerShell, from the TWIN A project folder, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
```

You will see:

```text
Enter the OBS WebSocket password (it will not be displayed)
```

Type the password you configured in OBS and press **Enter**.

Nothing appears while you type. That is expected.

TWIN A stores the password in your current Windows user environment as:

```text
TWINA_OBS_PASSWORD
```

The password is **not** written into the Git repository.

---

## 4. Optional — enable OBS Replay Buffer

If you want **Save Clip / Replay Buffer** controls:

1. In OBS open **Settings**.
2. Open **Output**.
3. Enable **Replay Buffer**.
4. Choose your desired replay duration.
5. Apply the settings.

TWIN A can then start/stop the Replay Buffer and save replay clips.

---

# Step 5 — Run TWIN A on the PC

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Two parts will start.

## Desktop Agent

A separate PowerShell window should open for the Desktop Agent.

Keep this window running.

The Desktop Agent is responsible for interactive Windows actions such as audio, desktop commands and other user-session functionality.

## Control Server

The original PowerShell window runs the TWIN A server.

It should listen locally on:

```text
http://127.0.0.1:5055
```

Keep this window running too.

---

# Step 6 — Test the installation

Before connecting the iPad, test TWIN A locally.

## 1. Open TWIN A on the PC

Open a browser on the PC and visit:

```text
http://127.0.0.1:5055
```

You should see the TWIN A interface.

---

## 2. Check the health endpoint

Open:

```text
http://127.0.0.1:5055/api/health
```

You should receive a small JSON response showing that the service is running.

---

## 3. Run the automatic smoke test

Keep TWIN A running.

Open a **second** PowerShell window.

Go to the project:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

The test checks important read-only parts of the platform without deliberately changing your files, OBS, games or IoT devices.

A healthy result contains lines such as:

```text
[PASS] Health
[PASS] Live state
[PASS] System details
[PASS] Audio endpoints
[PASS] Game library discovery
[PASS] Drive discovery
[PASS] Developer projects
[PASS] Flows
[PASS] Settings
[PASS] IoT state endpoint
```

If OBS is closed, a warning that OBS is not ready is normal.

---

# Step 7 — Connect the PC to Tailscale

TWIN A intentionally does not expose `5055` directly to your normal network or the public internet.

Make sure Tailscale is connected on the PC.

Run:

```powershell
tailscale status
```

If necessary:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```

Your Windows computer should appear as an active Tailscale device.

---

# Step 8 — Connect the iPad to Tailscale

On the iPad:

1. Open the **App Store**.
2. Search for **Tailscale**.
3. Install the official Tailscale app.
4. Open Tailscale.
5. Sign in using the **same Tailscale account/tailnet used on the PC**.
6. iPadOS will ask for permission to add a VPN configuration.
7. Tap **Allow**.
8. Enter the iPad passcode if requested.
9. Make sure Tailscale shows **Connected**.

The PC and iPad do not need to use the same physical network.

For example, this is fine:

```text
PC   → Ethernet → Internet
iPad → Wi-Fi    → Internet
```

Tailscale creates the private link between them.

---

# Step 9 — Publish TWIN A privately with Tailscale Serve

TWIN A must already be running before you do this.

Open another PowerShell window on the PC.

Run:

```powershell
tailscale serve --bg 5055
```

If that command is not found:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" serve --bg 5055
```

Tailscale should show an HTTPS address similar to:

```text
https://your-pc.some-tailnet.ts.net
```

and indicate that it proxies to the local TWIN A server.

Check the configuration with:

```powershell
tailscale serve status
```

or:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" serve status
```

You want the Serve configuration to point to:

```text
http://127.0.0.1:5055
```

## Important

Do **not** use:

```text
http://127.0.0.1:5055
```

on the iPad.

On the iPad, `127.0.0.1` means the iPad itself.

Do **not** expose port `5055` with router port forwarding.

Use the private `.ts.net` HTTPS address from Tailscale Serve.

---

# Step 10 — Open TWIN A on the iPad

Make sure:

- TWIN A is running on the PC
- the Desktop Agent window is running
- Tailscale is connected on the PC
- Tailscale is connected on the iPad
- Tailscale Serve is configured

On the iPad:

1. Open **Safari**.
2. Enter the `.ts.net` address provided by Tailscale Serve.
3. TWIN A should load.

Example format:

```text
https://your-pc.your-tailnet.ts.net
```

The exact name will be different for every Tailscale network.

---

# Step 11 — Install TWIN A on the iPad Home Screen

Once TWIN A works correctly in Safari:

1. Tap Safari's **Share** button.
2. Choose **Add to Home Screen**.
3. If shown, enable **Open as Web App**.
4. Name it:

```text
TWIN A
```

5. Tap **Add**.

TWIN A now appears on the iPad Home Screen like an installed app.

From then on, you can normally launch TWIN A by tapping its icon.

---

# First-use checklist

Before relying on TWIN A, verify the basics.

## Home

Check:

- CPU changes
- RAM changes
- GPU changes if supported
- GPU temperature if supported
- volume level matches Windows
- Volume + works
- Volume - works
- Mute works
- Screenshot creates a real file
- Steam launches
- OBS status is correct

## Studio

With OBS open:

- Start Recording
- Pause
- Resume
- Stop Recording
- Start Replay Buffer
- Save Clip
- Stop Replay Buffer
- switch OBS scenes
- mute/unmute OBS inputs

Watch OBS on the PC while testing the controls.

## Audio

Verify:

- Windows volume is live
- audio endpoints appear
- output switching works
- microphone endpoint appears
- OBS audio controls match OBS

## Games

Verify:

- Steam Library opens
- installed Steam games appear
- a game launches
- TWIN A detects the launch when verification is possible

## System

Verify:

- primary physical network adapter is shown
- download/upload activity changes when the PC uses the internet
- drives appear
- uptime is reasonable

## Files

Start with harmless files.

Test:

- open a folder
- download a small file
- upload a small file
- create a test folder
- rename the test folder
- delete the test folder

Do not experiment with deletion inside Windows or Program Files directories.

---

# What each tab does

## Home

The Home tab gives fast access to the controls normally needed most often.

It includes monitoring and common actions such as:

- CPU/GPU/RAM
- GPU temperature
- network information
- OBS recording
- Replay save
- screenshots
- Steam
- Discord
- Windows audio

---

## Studio

The Studio tab is the OBS control surface.

It can:

- connect to OBS WebSocket
- show real OBS connection state
- start/stop recording
- pause/resume recording
- start/stop Replay Buffer
- save a clip
- discover OBS scenes automatically
- change the Program scene
- discover OBS audio-capable inputs
- mute/unmute OBS inputs
- save recording/session markers

Scene names are read from OBS rather than being permanently hardcoded.

---

## Audio

The Audio tab controls Windows and OBS audio.

Depending on the PC it can show:

- current Windows master volume
- Windows mute state
- playback devices
- microphone/capture devices
- default output
- default microphone
- OBS audio inputs
- Discord shortcuts
- TWIN A soundboard

Some Discord controls are reported as **EXECUTED / STATE UNVERIFIED** because Discord does not provide TWIN A with a reliable supported readback API for every action.

That is intentional.

---

## Games

The Games tab automatically scans Steam.

It can:

- find Steam using Windows configuration
- discover Steam libraries
- find installed games
- open Steam directly to Library
- launch Steam games by App ID
- verify game launch when a suitable process can be found
- create custom non-Steam game entries
- save per-game profiles

Game profiles can include options such as:

- ensure OBS is running
- ensure Discord is running
- choose an audio output
- set Windows volume
- switch an OBS scene
- start Replay Buffer
- start recording

---

## System

The System tab provides PC information and Windows actions.

It can show:

- CPU
- GPU
- RAM
- GPU temperature
- active physical network adapter
- link speed
- download activity
- upload activity
- Windows uptime
- operating-system information
- disk/free-space information

TWIN A tries to ignore virtual/tunnel adapters such as:

- Tailscale
- VirtualBox
- VMware
- Hyper-V
- VPN/tunnel adapters

when selecting the main physical internet adapter.

---

## Files

The Files tab is a real file browser.

It can expose ready local fixed/removable drives available to your Windows account.

Supported actions include:

- browse
- open on the PC
- download to the iPad
- upload from the iPad
- create folder
- rename
- copy
- move
- delete

### Warning

File operations are real.

TWIN A contains guard rails for important Windows/system locations, but you should still treat delete/move/rename operations carefully.

Do not disable system-file protection unless you understand the risk.

---

## Dev

The Dev tab is for software projects.

It can:

- store project definitions
- show Git branch/status
- open a project folder
- open a configured IDE
- run a configured build command
- run tests
- run the project
- show command output
- detect tools such as .NET, Node, Git and Docker

Docker is optional. If Docker is not installed, TWIN A should say so instead of pretending that it is available.

---

## IoT

IoT is optional.

If no MQTT system is configured, TWIN A should truthfully show:

```text
NOT CONFIGURED
0 devices
```

It does not invent fake temperature or device data.

If you later have:

- an MQTT broker
- ESP32 devices
- sensors
- switches

you can configure them in TWIN A.

For MQTT authentication, keep the MQTT password outside the repository using:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-mqtt-password.ps1
```

---

## Flows

Flows let several commands run as one workflow.

Example:

```text
Open OBS
↓
Wait for OBS
↓
Switch scene
↓
Start Replay Buffer
↓
Set Windows volume
↓
Open Discord
↓
Launch a game
↓
Wait for game
↓
Start recording
```

A workflow result is based on the results of its steps.

A flow should not be shown as fully verified if one of its important steps failed or could not be verified.

---

## Settings

Settings stores machine-specific TWIN A configuration.

The main configuration file is:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

This file is **not supposed to be part of the Git repository**.

Passwords are kept separately.

---

# Normal daily use

After the first installation, normal use is much shorter.

## On the PC

1. Make sure Tailscale is connected.
2. If you want OBS controls, open OBS Studio.
3. Open PowerShell.
4. Go to TWIN A:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
```

5. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

6. Keep the Control Server and Desktop Agent running.

Normally you do not need to run `tailscale serve --bg 5055` every day if the Serve configuration is still present.

Check with:

```powershell
tailscale serve status
```

If there is no Serve configuration, run:

```powershell
tailscale serve --bg 5055
```

## On the iPad

1. Make sure Tailscale is connected.
2. Tap the **TWIN A** Home Screen icon.
3. Use the dashboard.

---

# Stopping TWIN A

To stop TWIN A:

1. Go to the PowerShell window running the Control Server.
2. Press:

```text
Ctrl + C
```

3. Close the Desktop Agent PowerShell window.

Tailscale itself can remain running.

You do not need to delete the Tailscale Serve configuration just because TWIN A is temporarily stopped.

---

# Updating TWIN A from GitHub

Before updating:

1. Stop TWIN A.
2. Close the Desktop Agent window.
3. Back up your configuration if it contains important custom settings.

Open PowerShell:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
```

Download the newest repository changes:

```powershell
git pull
```

Update frontend packages if needed:

```powershell
Set-Location ".\frontend"
npm install
Set-Location ".."
```

Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

If successful, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Then, in a second PowerShell window:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

If everything passes, reopen TWIN A on the iPad.

If the iPad still shows an old interface, completely close the TWIN A web app and reopen it.

---

# Backing up your personal TWIN A configuration

The GitHub repository stores the program source.

Your personal machine configuration is intentionally stored separately at:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

It may contain things such as:

- custom games
- game profiles
- Dev projects
- flows
- MQTT configuration
- UI/settings choices

## Back it up

Create a backup somewhere outside the TWIN A repository.

Examples:

- OneDrive
- Google Drive
- external USB drive
- NAS
- another private backup location

Example PowerShell command:

```powershell
New-Item -ItemType Directory -Force "$HOME\Documents\TwinABackup" | Out-Null

Copy-Item `
"$env:LOCALAPPDATA\TwinAControlCenter\config.json" `
"$HOME\Documents\TwinABackup\config.json" `
-Force
```

If you are preparing to format Windows, copy that backup to a location that will survive the format.

## Passwords are separate

The OBS password is stored as a Windows user environment variable:

```text
TWINA_OBS_PASSWORD
```

The optional MQTT password is stored as:

```text
TWINA_MQTT_PASSWORD
```

Those values are deliberately not stored in GitHub.

After reinstalling Windows, recreate them using the setup scripts.

---

# Restoring TWIN A after formatting Windows

This section is the complete recovery procedure.

## 1. Reinstall the required applications

Install:

- Git for Windows
- .NET 10 SDK
- Node.js 24
- Tailscale
- OBS Studio if used
- Steam if used
- Discord if used
- Rider if desired

---

## 2. Clone TWIN A again

Open PowerShell:

```powershell
New-Item -ItemType Directory -Force "$HOME\RiderProjects" | Out-Null
Set-Location "$HOME\RiderProjects"
```

Clone:

```powershell
git clone https://github.com/OWNER/TwinAControlCenter.git
```

Enter the project:

```powershell
Set-Location ".\TwinAControlCenter"
```

---

## 3. Restore your configuration backup

Create the local settings folder:

```powershell
New-Item `
-ItemType Directory `
-Force `
"$env:LOCALAPPDATA\TwinAControlCenter" |
Out-Null
```

Copy your backed-up `config.json` into:

```text
%LOCALAPPDATA%\TwinAControlCenter\config.json
```

Example:

```powershell
Copy-Item `
"E:\YourBackup\config.json" `
"$env:LOCALAPPDATA\TwinAControlCenter\config.json" `
-Force
```

Change `E:\YourBackup\config.json` to the real location of your backup.

If you do not have a backup, TWIN A can start with fresh/default configuration and you can reconfigure your custom games/projects/flows manually.

---

## 4. Configure OBS again

In OBS:

1. Tools
2. WebSocket Server Settings
3. enable WebSocket
4. port `4455`
5. enable authentication
6. set your password

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
```

---

## 5. Build TWIN A

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

---

## 6. Run TWIN A

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

---

## 7. Reconnect Tailscale

Sign in to Tailscale on Windows.

Make sure the iPad is signed into the same tailnet.

Check:

```powershell
tailscale status
```

---

## 8. Recreate Tailscale Serve

Run:

```powershell
tailscale serve --bg 5055
```

Then:

```powershell
tailscale serve status
```

Use the new `.ts.net` HTTPS address shown by Tailscale.

After a complete Windows reinstall, do not assume the old Tailscale device identity or URL will always be identical.

---

## 9. Run the smoke test

In another PowerShell window:

```powershell
Set-Location "$HOME\RiderProjects\TwinAControlCenter"

powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

---

## 10. Reopen TWIN A on the iPad

Open the `.ts.net` URL.

If the address changed after the Windows reinstall:

1. remove the old TWIN A Home Screen shortcut if necessary
2. open the new address in Safari
3. add it to the Home Screen again

The system is now restored.

---

# Optional — Start TWIN A automatically when you sign in to Windows

Do this only **after** the normal manual setup works correctly.

The Desktop Agent must run in your logged-in Windows session, so the safest simple automation is to start TWIN A when your user signs in.

## Using Task Scheduler

1. Press the Windows key.
2. Search for **Task Scheduler**.
3. Open it.
4. Choose **Create Task**.

### General tab

Name:

```text
TWIN A Control Center
```

Choose:

```text
Run only when user is logged on
```

Do not configure it as a hidden system service because the Desktop Agent needs access to your interactive Windows session.

### Triggers tab

1. Click **New**.
2. Begin the task: **At log on**.
3. Choose your Windows user.
4. Click **OK**.

### Actions tab

1. Click **New**.
2. Action: **Start a program**.
3. Program/script:

```text
powershell.exe
```

4. Add arguments:

```text
-ExecutionPolicy Bypass -File "%USERPROFILE%\RiderProjects\TwinAControlCenter\scripts\run.ps1"
```

If Task Scheduler does not expand `%USERPROFILE%` correctly in your environment, use the full real Windows path instead, for example:

```text
-ExecutionPolicy Bypass -File "C:\Users\YourName\RiderProjects\TwinAControlCenter\scripts\run.ps1"
```

5. Start in:

```text
C:\Users\YourName\RiderProjects\TwinAControlCenter
```

Use your real Windows username.

6. Save the task.

### Test it

Right-click the new task and choose **Run**.

Verify:

- the Desktop Agent opens
- the Control Server starts
- `http://127.0.0.1:5055` works
- the iPad can connect

If anything is wrong, delete the scheduled task and continue using the manual `run.ps1` method until the issue is fixed.

---

# Security

TWIN A controls real Windows actions, so security matters.

## Do not expose port 5055 publicly

Never configure router port forwarding such as:

```text
Internet → 5055 → PC
```

Do not change the server to listen publicly unless you understand the security consequences and have redesigned the authentication model.

The intended setup is:

```text
iPad
  │
  │ Tailscale
  ▼
private .ts.net URL
  │
  ▼
Tailscale Serve
  │
  ▼
127.0.0.1:5055
```

---

## Do not put passwords in GitHub

Never commit:

- OBS WebSocket password
- MQTT password
- API tokens
- private keys
- `.env` files containing secrets
- personal credentials

Use:

```powershell
.\scripts\set-obs-password.ps1
```

for OBS.

Use:

```powershell
.\scripts\set-mqtt-password.ps1
```

for MQTT.

---

## Treat the Files tab as a real file manager

Actions such as:

- delete
- rename
- move
- overwrite

affect real files.

System locations have protections, but the safest practice is still:

> Do not modify files you do not understand.

---

## Tailscale account security

Anyone authorized into your Tailscale network may have network-level reachability depending on your Tailscale access rules.

Protect the account used for Tailscale with strong authentication.

Do not casually add unknown devices/users to your tailnet.

---

# Troubleshooting

## `git` is not recognized

Example:

```text
'git' is not recognized...
```

Install Git for Windows.

Then close and reopen PowerShell.

Test:

```powershell
git --version
```

---

## `dotnet` is not recognized

Install the **.NET 10 SDK**.

Do not install only the ASP.NET Runtime if you are building from source.

After installation, close and reopen PowerShell.

Test:

```powershell
dotnet --version
```

---

## `node` or `npm` is not recognized

Install Node.js.

Close and reopen PowerShell.

Test:

```powershell
node --version
npm --version
```

---

## Build fails

Always run the build from the repository root:

```text
...\TwinAControlCenter
```

Then:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Read the **first real compiler/error message**, not only the final PowerShell exception.

If the frontend dependencies seem broken:

```powershell
Set-Location ".\frontend"
npm install
Set-Location ".."
```

Then rebuild.

---

## TWIN A opens on the PC but not the iPad

Check all of these.

### PC

```powershell
tailscale status
```

Then:

```powershell
tailscale serve status
```

TWIN A must also be running:

```text
http://127.0.0.1:5055
```

### iPad

- Tailscale installed
- Tailscale connected
- signed into the correct tailnet
- Safari uses the `.ts.net` URL, not `127.0.0.1`

---

## Tailscale command is not recognized

Use the full Windows path:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" status
```

For Serve:

```powershell
& "C:\Program Files\Tailscale\tailscale.exe" serve --bg 5055
```

---

## OBS shows offline

Check:

1. OBS is actually open.
2. OBS WebSocket is enabled.
3. port is `4455`.
4. authentication is enabled.
5. TWIN A has the correct password.

Run the password setup again:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
```

Then restart TWIN A.

---

## OBS is online but Replay Buffer fails

Enable Replay Buffer inside OBS:

```text
Settings → Output → Replay Buffer
```

Then try again.

---

## Desktop Agent is offline

TWIN A needs both the Control Server and Desktop Agent.

Stop TWIN A and start it again with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

A separate Desktop Agent PowerShell window should remain open.

---

## The volume changes but the UI does not

Wait a moment for the live state to refresh.

If it remains wrong:

1. verify the Desktop Agent is running
2. run the smoke test
3. restart TWIN A

---

## Steam games are missing

Make sure Steam is installed and has completed its normal setup.

Open Steam once before starting TWIN A.

TWIN A discovers Steam libraries from the Steam installation configuration and installed game manifests.

---

## Network shows the wrong adapter

The application tries to exclude common virtual/tunnel adapters.

If your physical adapter is still not selected correctly, inspect Windows adapters:

```powershell
Get-NetAdapter |
Where-Object Status -eq "Up" |
Select-Object Name,InterfaceDescription,LinkSpeed
```

---

## GPU information is missing

Current GPU usage/temperature support is primarily designed around NVIDIA's `nvidia-smi`.

Test:

```powershell
nvidia-smi --query-gpu=name,utilization.gpu,temperature.gpu --format=csv,noheader
```

If `nvidia-smi` is unavailable, NVIDIA GPU telemetry may not be available even though the rest of TWIN A still works.

---

## Port 5055 is already being used

Check:

```powershell
Get-NetTCPConnection -LocalPort 5055 -ErrorAction SilentlyContinue
```

Often this means an older TWIN A Control Server is still running.

Close old TWIN A/PowerShell windows and try again.

Avoid killing unrelated `dotnet` processes unless you know what they belong to.

---

## Smoke test says OBS offline

That is not automatically a failure.

If OBS is intentionally closed, the warning is expected.

Open OBS and retry if you want to test OBS connectivity.

---

## IoT says NOT CONFIGURED

That is correct when no MQTT broker/devices have been configured.

TWIN A intentionally does not create fake IoT devices.

---

# Advanced and optional features

## MQTT password

If you later configure an MQTT broker that requires a password:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-mqtt-password.ps1
```

The password is stored in the Windows user environment as:

```text
TWINA_MQTT_PASSWORD
```

Do not commit it to Git.

---

## Rider

Rider is not required simply to run TWIN A.

If you want to edit the source or use Rider through the Dev tab, install JetBrains Rider and configure the desired project/IDE path in TWIN A.

---

## Docker

Docker is not required.

If Docker is not installed, the Dev tab should report that it is not installed.

---

# Project folders

For users who are curious, the repository is organized approximately like this:

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

### Frontend

The frontend is the interface shown on the iPad.

It is an Angular Progressive Web App.

### Control Server

The Control Server provides the local API, live state and integrations.

### Desktop Agent

The Desktop Agent runs in the logged-in Windows session and performs interactive Windows actions that the server should not perform as a background-only process.

---

# Quick install summary

If you already understand the detailed instructions, the complete first-time process is:

```text
1. Install Git
2. Install .NET 10 SDK
3. Install Node.js 24
4. Install Tailscale
5. Install OBS / Steam / Discord if wanted
6. Clone the GitHub repository
7. Build TWIN A
8. Enable OBS WebSocket and save the OBS password
9. Run TWIN A
10. Run the smoke test
11. Sign in to the same Tailscale tailnet on PC and iPad
12. Run: tailscale serve --bg 5055
13. Open the generated .ts.net URL on the iPad
14. Add TWIN A to the iPad Home Screen
```

Commands from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-obs-password.ps1
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

In another PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

Tailscale:

```powershell
tailscale serve --bg 5055
```

Then open the private `.ts.net` address on the iPad.

---

# Final notes

TWIN A is a local/private PC-control project, not a public remote-administration server.

Its intended security model is:

- the server stays on localhost
- Tailscale handles the private device-to-device connection
- passwords stay on the Windows machine
- sensitive settings are kept outside the Git repository
- actions report honest verification status
- destructive file/system operations are treated carefully

If you are installing TWIN A for the first time, follow this README from the top in order and do not skip the local PC test before setting up the iPad.
