# TWIN A Control Center v0.9.0

Release validation for v0.9.0 requires all of the following before the GitHub Release is published:

- Windows build succeeds.
- Repository privacy check succeeds.
- Production npm dependency audit reports no high-or-critical production vulnerabilities.
- Windows installer builds successfully.
- Silent installer smoke test succeeds.
- Core installed executables are present.
- The TWIN A launcher exposes the embedded application icon used by the desktop shortcut.
- The desktop shortcut is created and targets the installed launcher.
- The built-in multi-tab Help Center and its Start Menu shortcut are installed.
- Remote Screen pinch zoom controls are absent.
- Two-finger Remote Screen scrolling remains present.

## Highlights

- Removed Remote Screen pinch zoom, including the + / - controls and zoom percentage UI.
- Preserved two-finger scrolling and the existing remote mouse/keyboard gestures.
- Added TWIN A branding to the Windows launcher and desktop shortcut.
- Added a built-in Help Center covering installation, iPad/Tailscale, OBS, Remote Screen, audio/Discord, files, games, Dev, status/security, troubleshooting, updates, and recovery.
- Cleaned known .NET analyzer warnings and hardened GitHub CI/release verification.
