from pathlib import Path
import re


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8").replace("\r\n", "\n")


def write(path: str, text: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text.replace("\r\n", "\n"), encoding="utf-8", newline="\n")


def replace_exact(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one match in {path}, found {count}. Needle: {old[:180]!r}")
    write(path, text.replace(old, new, 1))


def replace_count(path: str, old: str, new: str, expected: int) -> None:
    text = read(path)
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"Expected {expected} matches in {path}, found {count} for {old!r}")
    write(path, text.replace(old, new))


def replace_regex(path: str, pattern: str, new: str) -> None:
    text = read(path)
    updated, count = re.subn(pattern, new, text, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"Expected one regex match in {path}, found {count}: {pattern}")
    write(path, updated)


# Frontend: friendly Flow presets + useful wake-lock preference.
ts = "frontend/src/app/app.component.ts"
replace_exact(ts, "FlowConfig, GameInfo, GameProfile", "FlowConfig, FlowStep, GameInfo, GameProfile")
replace_exact(
    ts,
    "interface Toast{title:string;detail:string;status:'success'|'warning'|'error';}\n",
    "interface Toast{title:string;detail:string;status:'success'|'warning'|'error';}\n"
    "interface FlowStepPreset{key:string;label:string;group:string;description:string;command:string;payload:Record<string,string>;}\n",
)
replace_exact(
    ts,
    "  readonly flows=signal<FlowConfig[]>([]); flowEditorOpen=false; flowDraft:FlowConfig=this.blankFlow();\n",
    """  readonly flows=signal<FlowConfig[]>([]); flowEditorOpen=false; flowDraft:FlowConfig=this.blankFlow();
  readonly flowStepPresets:FlowStepPreset[]=[
    {key:'obs-open',group:'Apps',label:'Open OBS Studio',description:'Launch OBS and verify that its process is running.',command:'app.launch',payload:{app:'obs'}},
    {key:'steam-open',group:'Apps',label:'Open Steam',description:'Launch Steam.',command:'app.launch',payload:{app:'steam'}},
    {key:'discord-open',group:'Apps',label:'Open Discord',description:'Launch Discord.',command:'app.launch',payload:{app:'discord'}},
    {key:'taskmgr-open',group:'Apps',label:'Open Task Manager',description:'Open Windows Task Manager on the PC.',command:'app.launch',payload:{app:'taskmgr'}},
    {key:'steam-library',group:'Apps',label:'Open Steam Library',description:'Open Steam directly to Library.',command:'game.library.open',payload:{}},
    {key:'record-start',group:'OBS',label:'Start recording',description:'Ensure OBS recording is on.',command:'obs.record.start',payload:{}},
    {key:'record-stop',group:'OBS',label:'Stop recording',description:'Ensure OBS recording is off.',command:'obs.record.stop',payload:{}},
    {key:'record-pause',group:'OBS',label:'Pause / resume recording',description:'Toggle the current OBS recording pause state.',command:'obs.record.pause',payload:{}},
    {key:'replay-start',group:'OBS',label:'Start Replay Buffer',description:'Ensure the OBS Replay Buffer is on.',command:'obs.replay.start',payload:{}},
    {key:'replay-stop',group:'OBS',label:'Stop Replay Buffer',description:'Ensure the OBS Replay Buffer is off.',command:'obs.replay.stop',payload:{}},
    {key:'replay-save',group:'OBS',label:'Save replay clip',description:'Save the current OBS Replay Buffer clip.',command:'obs.replay.save',payload:{}},
    {key:'mic-toggle',group:'OBS',label:'Toggle microphone mute',description:'Mute or unmute the configured OBS microphone input.',command:'obs.mic.toggle',payload:{}},
    {key:'desktop-audio-toggle',group:'OBS',label:'Toggle desktop audio mute',description:'Mute or unmute the configured OBS desktop audio input.',command:'obs.desktop.toggle',payload:{}},
    {key:'volume-25',group:'Audio',label:'Set volume to 25%',description:'Set Windows master volume to 25%.',command:'audio.master.set',payload:{value:'25'}},
    {key:'volume-50',group:'Audio',label:'Set volume to 50%',description:'Set Windows master volume to 50%.',command:'audio.master.set',payload:{value:'50'}},
    {key:'volume-75',group:'Audio',label:'Set volume to 75%',description:'Set Windows master volume to 75%.',command:'audio.master.set',payload:{value:'75'}},
    {key:'volume-100',group:'Audio',label:'Set volume to 100%',description:'Set Windows master volume to 100%.',command:'audio.master.set',payload:{value:'100'}},
    {key:'volume-mute',group:'Audio',label:'Mute / unmute Windows audio',description:'Toggle Windows master mute.',command:'audio.master.mute',payload:{}},
    {key:'screenshot',group:'Windows',label:'Take screenshot',description:'Capture the Windows desktop and verify the saved file.',command:'windows.screenshot',payload:{}},
    {key:'show-desktop',group:'Windows',label:'Show desktop',description:'Send Win + D to show the Windows desktop.',command:'windows.desktop.show',payload:{}},
    {key:'lock-pc',group:'Windows',label:'Lock PC',description:'Lock the current Windows session.',command:'windows.lock',payload:{}},
    {key:'discord-mute',group:'Discord',label:'Toggle mute',description:'Send Discord mute toggle. Discord does not expose reliable final-state readback.',command:'discord.mute.toggle',payload:{}},
    {key:'discord-deafen',group:'Discord',label:'Toggle deafen',description:'Send Discord deafen toggle. Discord does not expose reliable final-state readback.',command:'discord.deafen.toggle',payload:{}}
  ];
  flowPresetToAdd='obs-open';
""",
)
replace_exact(
    ts,
    """  openFlowEditor(flow?:FlowConfig):void{this.flowDraft=flow?structuredClone(flow):this.blankFlow();this.flowEditorOpen=true;}
  addFlowStep():void{this.flowDraft.steps.push({command:'app.launch',payload:{app:'obs'},delayAfterMs:0,continueOnError:false});}
  removeFlowStep(i:number):void{this.flowDraft.steps.splice(i,1);}
  payloadText(step:any):string{return Object.entries(step.payload||{}).map(([k,v])=>`${k}=${v}`).join('; ');}
  setPayloadText(step:any,value:string):void{const payload:Record<string,string>={};for(const pair of value.split(';')){const idx=pair.indexOf('=');if(idx>0)payload[pair.slice(0,idx).trim()]=pair.slice(idx+1).trim();}step.payload=payload;}
""",
    """  openFlowEditor(flow?:FlowConfig):void{this.flowDraft=flow?structuredClone(flow):this.blankFlow();this.flowPresetToAdd='obs-open';this.flowEditorOpen=true;}
  addFlowStep():void{const preset=this.flowStepPresets.find(p=>p.key===this.flowPresetToAdd)??this.flowStepPresets[0];this.flowDraft.steps.push({command:preset.command,payload:structuredClone(preset.payload),delayAfterMs:0,continueOnError:false});}
  removeFlowStep(i:number):void{this.flowDraft.steps.splice(i,1);}
  flowPresetKey(step:FlowStep):string{return this.flowStepPresets.find(p=>p.command===step.command&&this.sameFlowPayload(p.payload,step.payload||{}))?.key??'__custom__';}
  setFlowPreset(step:FlowStep,key:string):void{if(key==='__custom__')return;const preset=this.flowStepPresets.find(p=>p.key===key);if(!preset)return;step.command=preset.command;step.payload=structuredClone(preset.payload);}
  flowStepLabel(step:FlowStep):string{const key=this.flowPresetKey(step);return this.flowStepPresets.find(p=>p.key===key)?.label??step.command;}
  flowStepDescription(step:FlowStep):string{const key=this.flowPresetKey(step);return this.flowStepPresets.find(p=>p.key===key)?.description??'Custom step. Review its command and payload under Advanced.';}
  payloadText(step:FlowStep):string{return Object.entries(step.payload||{}).map(([k,v])=>`${k}=${v}`).join('; ');}
  setPayloadText(step:FlowStep,value:string):void{const payload:Record<string,string>={};for(const pair of value.split(';')){const idx=pair.indexOf('=');if(idx>0)payload[pair.slice(0,idx).trim()]=pair.slice(idx+1).trim();}step.payload=payload;}
""",
)
replace_exact(
    ts,
    "  async refreshSettings():Promise<void>{try{this.settingsEnvelope.set(await this.control.get<SettingsEnvelope>('/api/settings'));this.applyHomeLayout();}catch(e){this.showToast('Settings failed',this.err(e),'error');}}\n",
    "  async refreshSettings():Promise<void>{try{this.settingsEnvelope.set(await this.control.get<SettingsEnvelope>('/api/settings'));this.applyHomeLayout();this.syncWakeLockPreference();}catch(e){this.showToast('Settings failed',this.err(e),'error');}}\n",
)
replace_exact(
    ts,
    "  setUi(field:'confirmPowerActions'|'protectSystemPaths'|'enableRemoteControl',value:boolean):void{const env=this.settingsEnvelope();if(!env)return;(env.settings.ui as any)[field]=value;this.settingsEnvelope.set({...env,settings:{...env.settings,ui:{...env.settings.ui}}});}\n",
    "  setUi(field:'confirmPowerActions'|'protectSystemPaths'|'enableRemoteControl'|'keepScreenAwake',value:boolean):void{const env=this.settingsEnvelope();if(!env)return;(env.settings.ui as any)[field]=value;this.settingsEnvelope.set({...env,settings:{...env.settings,ui:{...env.settings.ui}}});if(field==='keepScreenAwake')this.syncWakeLockPreference();}\n",
)
replace_exact(
    ts,
    "  sourceIcon(name:string):string{const v=name.toLowerCase();if(v.includes('mic'))return'🎙';if(v.includes('desktop'))return'◖';if(v.includes('game'))return'◇';return'◉';}\n",
    """  private syncWakeLockPreference():void{const enabled=this.settingsEnvelope()?.settings.ui.keepScreenAwake??true;try{localStorage.setItem('twina.keepScreenAwake',String(enabled));}catch{}window.dispatchEvent(new CustomEvent('twina-wake-lock-preference',{detail:{enabled}}));}
  private sameFlowPayload(a:Record<string,string>,b:Record<string,string>):boolean{const ak=Object.keys(a).sort();const bk=Object.keys(b).sort();return ak.length===bk.length&&ak.every((key,i)=>key===bk[i]&&String(a[key])===String(b[key]));}

  sourceIcon(name:string):string{const v=name.toLowerCase();if(v.includes('mic'))return'🎙';if(v.includes('desktop'))return'◖';if(v.includes('game'))return'◇';return'◉';}
""",
)

control = "frontend/src/app/core/control.service.ts"
replace_exact(
    control,
    "  ui:{confirmPowerActions:boolean;protectSystemPaths:boolean;enableRemoteControl:boolean;homeCards:HomeCardPreference[]};\n",
    "  ui:{confirmPowerActions:boolean;protectSystemPaths:boolean;enableRemoteControl:boolean;keepScreenAwake:boolean;homeCards:HomeCardPreference[]};\n",
)

# HTML: dock Help in the rail, show readable Flow names, add wake setting, simplify Flow editor.
html = "frontend/src/app/app.component.html"
replace_exact(
    html,
    "    </nav>\n  </aside>\n",
    "    </nav>\n    <a class=\"rail-help\" href=\"help/index.html\" aria-label=\"Open TWIN A Help Center\" title=\"Help Center\"><span aria-hidden=\"true\">?</span><b>Help</b></a>\n  </aside>\n",
)
replace_exact(html, "{{step.command}}{{last?'':' → '}}", "{{flowStepLabel(step)}}{{last?'':' → '}}")
replace_exact(
    html,
    "<label class=\"toggle-row\"><span><b>Protect Windows/system paths</b><small>Browse/download allowed, destructive mutations blocked.</small></span><input type=\"checkbox\" [ngModel]=\"env.settings.ui.protectSystemPaths\" (ngModelChange)=\"setUi('protectSystemPaths',$event)\"></label></article>\n",
    "<label class=\"toggle-row\"><span><b>Protect Windows/system paths</b><small>Browse/download allowed, destructive mutations blocked.</small></span><input type=\"checkbox\" [ngModel]=\"env.settings.ui.protectSystemPaths\" (ngModelChange)=\"setUi('protectSystemPaths',$event)\"></label><label class=\"toggle-row\"><span><b>Keep iPad screen awake</b><small>Keep the display awake while TWIN A is open and visible.</small></span><input type=\"checkbox\" [ngModel]=\"env.settings.ui.keepScreenAwake\" (ngModelChange)=\"setUi('keepScreenAwake',$event)\"></label></article>\n",
)

new_flow_editor = """    <!-- FLOW EDITOR -->
    <div class="modal-backdrop" *ngIf="flowEditorOpen">
      <section class="modal glass wide-modal flow-editor">
        <div class="modal-title"><div><small>WORKFLOW BUILDER</small><h2>{{flowDraft.id?'Edit flow':'New flow'}}</h2><p>Pick ready-made actions. Open Advanced only when you need it.</p></div><button (click)="flowEditorOpen=false">×</button></div>
        <div class="form-grid"><label><span>Name</span><input [(ngModel)]="flowDraft.name"></label><label><span>Category</span><input [(ngModel)]="flowDraft.category" placeholder="GAMING, RECORDING, DAILY..."></label></div>
        <div class="flow-add-bar"><select [(ngModel)]="flowPresetToAdd"><option *ngFor="let preset of flowStepPresets" [value]="preset.key">{{preset.group}} · {{preset.label}}</option></select><button class="primary-action compact" (click)="addFlowStep()">＋ Add step</button></div>
        <div class="flow-step-editor">
          <div class="flow-step-row preset-row" *ngFor="let step of flowDraft.steps;let i=index">
            <span class="flow-step-number">{{i+1}}</span>
            <div class="flow-step-main">
              <select class="flow-preset-select" [ngModel]="flowPresetKey(step)" (ngModelChange)="setFlowPreset(step,$event)"><option *ngFor="let preset of flowStepPresets" [value]="preset.key">{{preset.group}} · {{preset.label}}</option><option value="__custom__">Advanced · Existing/custom command</option></select>
              <small>{{flowStepDescription(step)}}</small>
              <details class="flow-step-advanced"><summary>Timing & advanced options</summary><div class="flow-advanced-grid"><label><span>Delay after (ms)</span><input type="number" min="0" max="30000" [(ngModel)]="step.delayAfterMs"></label><label class="tiny-check friendly-check"><input type="checkbox" [(ngModel)]="step.continueOnError"><span>Continue if this step fails</span></label><ng-container *ngIf="flowPresetKey(step)==='__custom__'"><label><span>Command</span><input [(ngModel)]="step.command"></label><label><span>Payload</span><input [ngModel]="payloadText(step)" (ngModelChange)="setPayloadText(step,$event)" placeholder="key=value; key2=value"></label></ng-container></div></details>
            </div>
            <button class="danger-mini flow-remove" (click)="removeFlowStep(i)" aria-label="Remove step">×</button>
          </div>
        </div>
        <div class="empty-state flow-empty" *ngIf="!flowDraft.steps.length">Choose an action above and add your first step.</div>
        <div class="modal-actions"><button class="secondary-action" (click)="flowEditorOpen=false">Cancel</button><button class="primary-action compact" (click)="saveFlow()">Save flow</button></div>
      </section>
    </div>
"""
replace_regex(html, r"    <!-- FLOW EDITOR -->.*?(?=\n  </main>)", new_flow_editor)

index = "frontend/src/index.html"
replace_exact(
    index,
    "  <ta-root></ta-root>\n  <a class=\"twina-help-launcher\" href=\"help/index.html\" aria-label=\"Open TWIN A Help Center\" title=\"Help Center\"><span aria-hidden=\"true\">?</span><b>HELP</b></a>\n",
    "  <ta-root></ta-root>\n",
)

styles = "frontend/src/styles.css"
replace_exact(styles, "@import './app/remote-v08.css';", "@import './app/remote-v08.css';\n@import './app/ux-v092.css';")

ux_css = """/* v0.9.2 UX refinements: docked Help + friendly Flow builder */
.rail nav{flex:1;min-height:0}
.rail-help{margin-top:10px;width:64px;min-height:46px;flex:0 0 auto;border:1px solid rgba(105,203,255,.24);border-radius:13px;display:flex;align-items:center;justify-content:center;gap:5px;text-decoration:none;color:#a9dfff;background:rgba(22,143,235,.08);font-size:9px;font-weight:800;letter-spacing:.06em;transition:.18s ease}
.rail-help span{font-size:16px;line-height:1}.rail-help:hover{background:rgba(22,143,235,.16);color:#fff;transform:translateY(-1px)}.rail-help:focus-visible{outline:2px solid var(--cyan);outline-offset:2px}
.flow-editor .modal-title p{margin:7px 0 0;color:#71829a;font-size:10px;line-height:1.45}.flow-add-bar{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:10px;align-items:center;margin:16px 0 12px;padding:12px;border:1px solid rgba(255,255,255,.07);border-radius:15px;background:rgba(255,255,255,.025)}
.flow-add-bar select,.flow-preset-select,.flow-advanced-grid input{width:100%;min-height:44px;border:1px solid rgba(255,255,255,.08);border-radius:11px;background:#09111e;color:#e9f4ff;padding:0 12px;outline:none}.flow-add-bar select:focus,.flow-preset-select:focus,.flow-advanced-grid input:focus{border-color:rgba(51,177,255,.4)}
.flow-step-row.preset-row{grid-template-columns:34px minmax(0,1fr) 34px!important;align-items:start!important;gap:10px!important;padding:12px 0;border-bottom:1px solid rgba(255,255,255,.05)}.flow-step-row.preset-row:last-child{border-bottom:0}.flow-step-row.preset-row>.flow-step-number{width:30px;height:30px;border-radius:9px;display:grid;place-items:center;background:rgba(36,156,255,.1);color:#7dccff;font-weight:800}
.flow-step-main{min-width:0;display:flex;flex-direction:column;gap:7px}.flow-step-main>small{color:#74859d;line-height:1.45}.flow-step-advanced{border-top:1px solid rgba(255,255,255,.06);padding-top:7px}.flow-step-advanced summary{cursor:pointer;color:#7ea8c5;font-size:10px;user-select:none}.flow-advanced-grid{display:grid;grid-template-columns:1fr 1fr;gap:9px;margin-top:10px}.flow-advanced-grid label{display:flex;flex-direction:column;gap:5px}.flow-advanced-grid label>span{font-size:9px;color:#71829b}.flow-advanced-grid .friendly-check{flex-direction:row;align-items:center;align-self:end;min-height:44px}.flow-advanced-grid .friendly-check input{width:auto;min-height:auto}.flow-remove{margin-top:4px}.flow-empty{margin:10px 0 0}
@media(max-width:760px){.flow-add-bar{grid-template-columns:1fr}.flow-advanced-grid{grid-template-columns:1fr}.flow-step-row.preset-row{grid-template-columns:30px minmax(0,1fr) 32px!important}.flow-step-row.preset-row .flow-step-main input{display:block!important}.flow-step-row.preset-row .tiny-check{display:flex!important}}
"""
write("frontend/src/app/ux-v092.css", ux_css)

# Wake Lock obeys the new preference, defaulting to on for the iPad control-panel use case.
wake = """type ScreenWakeLockSentinel = {
  readonly released: boolean;
  release(): Promise<void>;
  addEventListener(type: 'release', listener: () => void): void;
};

type NavigatorWithWakeLock = Navigator & {
  wakeLock?: { request(type: 'screen'): Promise<ScreenWakeLockSentinel>; };
};

const preferenceKey = 'twina.keepScreenAwake';
let sentinel: ScreenWakeLockSentinel | null = null;
let requestInProgress = false;
let installed = false;
let preferenceEnabled = true;

try { preferenceEnabled = localStorage.getItem(preferenceKey) !== 'false'; } catch { }

async function releaseScreenWakeLock(): Promise<void> {
  const active = sentinel;
  sentinel = null;
  if (active && !active.released) { try { await active.release(); } catch { } }
}

async function requestScreenWakeLock(): Promise<void> {
  if (!preferenceEnabled) { await releaseScreenWakeLock(); return; }
  if (document.visibilityState !== 'visible' || requestInProgress) return;
  if (sentinel && !sentinel.released) return;
  const nav = navigator as NavigatorWithWakeLock;
  if (!nav.wakeLock) return;
  requestInProgress = true;
  try {
    const lock = await nav.wakeLock.request('screen');
    sentinel = lock;
    lock.addEventListener('release', () => { if (sentinel === lock) sentinel = null; });
  } catch { sentinel = null; }
  finally { requestInProgress = false; }
}

export function installScreenWakeLock(): void {
  if (installed) return;
  installed = true;
  const retry = (): void => { void requestScreenWakeLock(); };
  window.addEventListener('twina-wake-lock-preference', (event: Event) => {
    const enabled = Boolean((event as CustomEvent<{enabled:boolean}>).detail?.enabled);
    preferenceEnabled = enabled;
    try { localStorage.setItem(preferenceKey, String(enabled)); } catch { }
    if (enabled) retry(); else void releaseScreenWakeLock();
  });
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') retry(); });
  window.addEventListener('pageshow', retry);
  window.addEventListener('focus', retry);
  document.addEventListener('pointerdown', retry, { passive: true });
  retry();
}
"""
write("frontend/src/wake-lock.ts", wake)

# Backend setting model.
model = "backend/TwinA.ControlServer/Models/AppConfiguration.cs"
replace_exact(
    model,
    "    public bool ProtectSystemPaths { get; set; } = true;\n    public bool EnableRemoteControl { get; set; }\n    public List<HomeCardPreference> HomeCards { get; set; } = HomeCardPreference.CreateDefaults();\n",
    "    public bool ProtectSystemPaths { get; set; } = true;\n    public bool EnableRemoteControl { get; set; }\n    public bool KeepScreenAwake { get; set; } = true;\n    public List<HomeCardPreference> HomeCards { get; set; } = HomeCardPreference.CreateDefaults();\n",
)

# Installer: use a real multi-resolution ICO at a versioned path for Shell shortcuts.
iss = "installer/TwinAControlCenter.iss"
replace_exact(iss, "UninstallDisplayIcon={app}\\launcher\\{#AppExeName}", "UninstallDisplayIcon={app}\\assets\\TwinA-{#AppVersion}.ico")
replace_exact(
    iss,
    "Source: \"{#SourceRoot}\\launcher\\*\"; DestDir: \"{app}\\launcher\"; Flags: ignoreversion recursesubdirs createallsubdirs\nSource: \"{#SourceRoot}\\README.md\"; DestDir: \"{app}\"; Flags: ignoreversion\n",
    "Source: \"{#SourceRoot}\\launcher\\*\"; DestDir: \"{app}\\launcher\"; Flags: ignoreversion recursesubdirs createallsubdirs\nSource: \"assets\\TwinA.ico\"; DestDir: \"{app}\\assets\"; DestName: \"TwinA-{#AppVersion}.ico\"; Flags: ignoreversion\nSource: \"{#SourceRoot}\\README.md\"; DestDir: \"{app}\"; Flags: ignoreversion\n",
)
replace_count(iss, 'IconFilename: "{app}\\launcher\\{#AppExeName}"', 'IconFilename: "{app}\\assets\\TwinA-{#AppVersion}.ico"', 5)

# Short, friendly README for viewers and normal users.
readme = """# TWIN A Control Center

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
cd .\\TwinAControlCenter
powershell -ExecutionPolicy Bypass -File .\\scripts\\build.ps1
```

Normal users should use the installer from **Releases** instead.

## Privacy

TWIN A binds its Control Server to `127.0.0.1` and is intended to be reached privately through Tailscale. Do not port-forward port `5055`.

Machine-specific configuration stays outside the repository under `%LOCALAPPDATA%\\TwinAControlCenter`.

See [LICENSE](LICENSE) for license terms.
"""
write("README.md", readme)

# Release version.
replace_exact("frontend/package.json", '"version": "0.9.1"', '"version": "0.9.2"')
replace_count("frontend/package-lock.json", '"version": "0.9.1"', '"version": "0.9.2"', 2)

# Rebuild a Windows-friendly ICO: 10 DIB frames from 16px through 256px.
from PIL import Image
image = Image.open("frontend/src/assets/icon-512.png").convert("RGBA")
sizes = [(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(96,96),(128,128),(256,256)]
image.save("installer/assets/TwinA.ico", format="ICO", sizes=sizes, bitmap_format="bmp")

print("TWIN A v0.9.2 UX/icon patch applied successfully.")
