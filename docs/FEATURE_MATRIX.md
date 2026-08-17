# TWIN A v0.5 feature / verification matrix

| Area | Real implementation | Verification level |
|---|---|---|
| CPU / RAM | Windows APIs | VERIFIED state feed |
| NVIDIA GPU / temperature | `nvidia-smi` | VERIFIED sensor query |
| Ethernet NET ↓ / ↑ | `NetworkInterface` byte counters | VERIFIED measurement |
| Windows volume / mute | Core Audio | VERIFIED readback |
| Windows audio device switch | Windows PolicyConfig + NAudio readback | VERIFIED default endpoint |
| OBS connection | OBS WebSocket v5 | VERIFIED |
| OBS record / pause / replay | OBS WebSocket state requests | VERIFIED when OBS confirms |
| OBS scene switch | Dynamic scene list + program readback | VERIFIED |
| OBS input mute | Input mute readback | VERIFIED |
| Replay save | OBS request + filesystem observation | VERIFIED if new file observed, otherwise UNVERIFIED |
| Screenshot | Desktop Agent + filesystem | VERIFIED |
| Steam library | `steam://open/games` + Steam process | EXECUTED / page state unverified |
| Steam game launch | App ID + process under install folder | VERIFIED when process found |
| Custom game launch | Configured target + optional process name | VERIFIED only when process configured/found |
| Discord mute/deafen | Native global hotkeys | EXECUTED / state unverified |
| Discord Soundboard open | Native shortcut | EXECUTED / UI state unverified |
| TWIN A soundboard | Windows media playback | EXECUTED / playback state unverified |
| File create/rename/move/copy/delete | Real filesystem | VERIFIED result/absence |
| File open on PC | Shell open | EXECUTED / window state unverified |
| File upload | Streamed to real filesystem | VERIFIED file existence |
| Dev build/test | Configured command + exit code | VERIFIED exit code |
| Dev run | New terminal | EXECUTED / long-running state unverified |
| Rider / Explorer | Shell launch + process/path validation | EXECUTED / exact UI state unverified |
| MQTT broker test | MQTT client login | VERIFIED connection |
| MQTT device command | Publish + optional state subscription | VERIFIED only with matching state payload |
| Flow | Sequential dispatcher | VERIFIED only if every step is verified |
