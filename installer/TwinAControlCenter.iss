#ifndef SourceRoot
  #define SourceRoot "..\artifacts\installer\payload"
#endif
#ifndef OutputRoot
  #define OutputRoot "..\artifacts\installer"
#endif
#ifndef AppVersion
  #define AppVersion "0.9.0-dev"
#endif

#define AppName "TWIN A Control Center"
#define AppPublisher "TWIN A"
#define AppExeName "TwinA.Launcher.exe"

[Setup]
AppId={{CBA70B99-91EA-47E6-90A9-7CB7CF6DF0A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\TWIN A Control Center
DefaultGroupName=TWIN A Control Center
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputRoot}
OutputBaseFilename=TwinA-Control-Center-Setup-{#AppVersion}-win-x64
SetupIconFile=assets\TwinA.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark
UninstallDisplayIcon={app}\launcher\{#AppExeName}
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "tailscale"; Description: "Install Tailscale (recommended — required for private iPad access outside localhost)"; GroupDescription: "Companion applications:"; Flags: checkedonce
Name: "obs"; Description: "Install OBS Studio (optional — enables recording and Studio controls)"; GroupDescription: "Companion applications:"
Name: "steam"; Description: "Install Steam (optional — enables automatic Steam library/game integration)"; GroupDescription: "Companion applications:"
Name: "discord"; Description: "Install Discord (optional — enables Discord shortcuts/integration)"; GroupDescription: "Companion applications:"
Name: "startup"; Description: "Start TWIN A automatically when I sign in to Windows"; GroupDescription: "TWIN A startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
Source: "{#SourceRoot}\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\agent\*"; DestDir: "{app}\agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\launcher\*"; DestDir: "{app}\launcher"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceRoot}\install-dependencies.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\TWIN A Control Center"; Filename: "{app}\launcher\{#AppExeName}"; Parameters: "--open"; WorkingDir: "{app}\launcher"; IconFilename: "{app}\launcher\{#AppExeName}"
Name: "{autoprograms}\TWIN A - Configure iPad Access"; Filename: "{app}\launcher\{#AppExeName}"; Parameters: "--setup"; WorkingDir: "{app}\launcher"; IconFilename: "{app}\launcher\{#AppExeName}"
Name: "{autoprograms}\TWIN A - Help Center"; Filename: "{app}\server\wwwroot\help\index.html"; IconFilename: "{app}\launcher\{#AppExeName}"
Name: "{autoprograms}\TWIN A - Read Me"; Filename: "{app}\README.md"; IconFilename: "{app}\launcher\{#AppExeName}"
Name: "{autodesktop}\TWIN A Control Center"; Filename: "{app}\launcher\{#AppExeName}"; Parameters: "--open"; WorkingDir: "{app}\launcher"; IconFilename: "{app}\launcher\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TwinAControlCenter"; ValueData: """{app}\launcher\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-dependencies.ps1"" -Tailscale"; StatusMsg: "Installing Tailscale if needed..."; Flags: runhidden waituntilterminated; Tasks: tailscale; Check: not IsUpdateMode
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-dependencies.ps1"" -Obs"; StatusMsg: "Installing OBS Studio if needed..."; Flags: runhidden waituntilterminated; Tasks: obs; Check: not IsUpdateMode
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-dependencies.ps1"" -Steam"; StatusMsg: "Installing Steam if needed..."; Flags: runhidden waituntilterminated; Tasks: steam; Check: not IsUpdateMode
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-dependencies.ps1"" -Discord"; StatusMsg: "Installing Discord if needed..."; Flags: runhidden waituntilterminated; Tasks: discord; Check: not IsUpdateMode
Filename: "{app}\launcher\{#AppExeName}"; Parameters: "--setup"; Description: "Start TWIN A and configure private iPad access"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateMode
Filename: "{app}\launcher\{#AppExeName}"; Parameters: "--open"; Flags: nowait; Check: IsUpdateMode
Filename: "{app}\server\wwwroot\help\index.html"; Description: "Open the TWIN A Help Center"; Flags: shellexec postinstall skipifsilent unchecked; Check: not IsUpdateMode

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM TwinA.Launcher.exe /T /F"; Flags: runhidden; RunOnceId: "StopTwinALauncher"
Filename: "taskkill.exe"; Parameters: "/IM TwinA.ControlServer.exe /T /F"; Flags: runhidden; RunOnceId: "StopTwinAServer"
Filename: "taskkill.exe"; Parameters: "/IM TwinA.DesktopAgent.exe /T /F"; Flags: runhidden; RunOnceId: "StopTwinAAgent"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function IsUpdateMode(): Boolean;
begin
  Result := CompareText(ExpandConstant('{param:TWINAUPDATE|0}'), '1') = 0;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
