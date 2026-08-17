# Security decisions — v0.5

1. The Control Server binds to `127.0.0.1:5055`, never the PC's public ISP address.
2. Tailscale Serve provides private HTTPS access; router port forwarding is not required.
3. The command API is allowlisted. There is no endpoint that accepts arbitrary shell/script text as a one-off remote command.
4. Interactive desktop operations are isolated in a named-pipe Desktop Agent running as the logged-in user.
5. OBS and MQTT passwords remain local Windows user environment secrets and are never returned to the PWA.
6. Power actions are confirmation-protected unless the user explicitly disables that guard rail.
7. All ready fixed/removable drives may be browsed, but Windows/System/Program Files paths and drive roots are protected from destructive mutation by default.
8. Developer projects may contain configured build/test/run commands. Treat adding/editing a Dev project as granting TWIN A permission to execute those exact persisted commands.
9. File/open, Discord shortcuts, sound playback, Rider and Explorer actions intentionally return UNVERIFIED when the target UI does not expose a trustworthy final state.
10. MQTT commands become VERIFIED only when a configured state topic reports the expected payload; publishing alone is not mislabeled as verified.
