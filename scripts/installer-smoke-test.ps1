param(
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Get-ChildItem -Path (Join-Path $Root 'artifacts\installer') -Filter '*.exe' -File |
        Select-Object -First 1 -ExpandProperty FullName
}

Assert-True (-not [string]::IsNullOrWhiteSpace($InstallerPath)) 'Installer executable was not found.'
Assert-True (Test-Path -LiteralPath $InstallerPath -PathType Leaf) "Installer executable does not exist: $InstallerPath"

# Static release guards: these identifiers belong only to the removed Remote Screen zoom feature.
$desktopTs = Get-Content (Join-Path $Root 'frontend\src\app\desktop-control.component.ts') -Raw
$desktopHtml = Get-Content (Join-Path $Root 'frontend\src\app\desktop-control.component.html') -Raw
$remoteCss = Get-Content (Join-Path $Root 'frontend\src\app\remote-v08.css') -Raw
$installerScript = Get-Content (Join-Path $Root 'installer\TwinAControlCenter.iss') -Raw
$launcherProject = Get-Content (Join-Path $Root 'backend\TwinA.Launcher\TwinA.Launcher.csproj') -Raw

foreach ($forbidden in @('remoteZoom', 'zoomToolsVisible', 'pinchStartDistance', 'pinchStartZoom')) {
    Assert-True (-not $desktopTs.Contains($forbidden)) "Removed zoom identifier is still present: $forbidden"
}
Assert-True (-not $desktopHtml.Contains('zoom-tools')) 'Removed zoom toolbar is still present in Remote Screen HTML.'
Assert-True (-not $remoteCss.Contains('.zoom-tools')) 'Removed zoom toolbar CSS is still present.'
Assert-True ($desktopHtml.Contains('2 FINGERS · SCROLL')) 'Two-finger scrolling help is missing from Remote Screen.'
Assert-True ($launcherProject.Contains('<ApplicationIcon>..\..\installer\assets\TwinA.ico</ApplicationIcon>')) 'Launcher is not configured to embed the TWIN A application icon.'
Assert-True ($installerScript.Contains('SetupIconFile=assets\TwinA.ico')) 'Installer is not configured to use the TWIN A setup icon.'
Assert-True ($installerScript.Contains('TWIN A - Help Center')) 'Installer does not expose the offline TWIN A Help Center shortcut.'

$installRoot = Join-Path $env:TEMP 'TwinAControlCenter-CI-Install'
Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue

$arguments = @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/TASKS="desktopicon"',
    "/DIR=`"$installRoot`""
)

Write-Host "Smoke-installing: $InstallerPath" -ForegroundColor Cyan
$install = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -Wait -PassThru
Assert-True ($install.ExitCode -eq 0) "Installer returned exit code $($install.ExitCode)."

$launcher = Join-Path $installRoot 'launcher\TwinA.Launcher.exe'
$server = Join-Path $installRoot 'server\TwinA.ControlServer.exe'
$agent = Join-Path $installRoot 'agent\TwinA.DesktopAgent.exe'
$help = Join-Path $installRoot 'server\wwwroot\help\index.html'

Assert-True (Test-Path $launcher -PathType Leaf) 'Installed launcher executable is missing.'
Assert-True (Test-Path $server -PathType Leaf) 'Installed Control Server executable is missing.'
Assert-True (Test-Path $agent -PathType Leaf) 'Installed Desktop Agent executable is missing.'
Assert-True (Test-Path $help -PathType Leaf) 'Installed Help Center is missing.'

$helpText = Get-Content $help -Raw
Assert-True ($helpText.Contains('TWIN A Help Center')) 'Installed Help Center content is invalid.'
Assert-True ($helpText.Contains('Troubleshooting')) 'Installed Help Center troubleshooting tab is missing.'
Assert-True ($helpText.Contains('Update & Recovery')) 'Installed Help Center recovery tab is missing.'

# Verify the launcher has an embedded application icon. The desktop shortcut uses this target icon.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($launcher)
try {
    Assert-True ($null -ne $icon) 'TWIN A launcher does not expose an associated application icon.'
    Assert-True ($icon.Width -gt 0 -and $icon.Height -gt 0) 'TWIN A launcher icon is invalid.'
}
finally {
    if ($null -ne $icon) { $icon.Dispose() }
}

$shell = New-Object -ComObject WScript.Shell
$desktopShortcutName = 'TWIN A Control Center.lnk'
$desktopCandidates = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('CommonDesktopDirectory')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$desktopShortcutPath = $desktopCandidates |
    ForEach-Object { Join-Path $_ $desktopShortcutName } |
    Where-Object { Test-Path $_ -PathType Leaf } |
    Select-Object -First 1

Assert-True (-not [string]::IsNullOrWhiteSpace($desktopShortcutPath)) 'Installer did not create the TWIN A desktop shortcut.'

$desktopShortcut = $shell.CreateShortcut($desktopShortcutPath)
$shortcutTarget = [IO.Path]::GetFullPath($desktopShortcut.TargetPath)
$expectedTarget = [IO.Path]::GetFullPath($launcher)
Assert-True ($shortcutTarget.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) "Desktop shortcut target is incorrect: $shortcutTarget"

$helpShortcutName = 'TWIN A - Help Center.lnk'
$programCandidates = @(
    [Environment]::GetFolderPath('Programs'),
    [Environment]::GetFolderPath('CommonPrograms')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$helpShortcutPath = $programCandidates |
    ForEach-Object { Join-Path $_ $helpShortcutName } |
    Where-Object { Test-Path $_ -PathType Leaf } |
    Select-Object -First 1

Assert-True (-not [string]::IsNullOrWhiteSpace($helpShortcutPath)) 'Installer did not create the TWIN A Help Center Start Menu shortcut.'
$helpShortcut = $shell.CreateShortcut($helpShortcutPath)
$helpShortcutTarget = [IO.Path]::GetFullPath($helpShortcut.TargetPath)
$expectedHelpTarget = [IO.Path]::GetFullPath($help)
Assert-True ($helpShortcutTarget.Equals($expectedHelpTarget, [StringComparison]::OrdinalIgnoreCase)) "Help Center shortcut target is incorrect: $helpShortcutTarget"

Write-Host 'Installer smoke test passed:' -ForegroundColor Green
Write-Host ' - core executables installed' -ForegroundColor Green
Write-Host ' - built-in multi-tab Help Center installed' -ForegroundColor Green
Write-Host ' - Help Center Start Menu shortcut created and verified' -ForegroundColor Green
Write-Host ' - Remote Screen zoom controls absent' -ForegroundColor Green
Write-Host ' - two-finger scroll retained' -ForegroundColor Green
Write-Host ' - launcher application icon embedded' -ForegroundColor Green
Write-Host ' - desktop shortcut created and targets TWIN A launcher' -ForegroundColor Green

$uninstaller = Join-Path $installRoot 'unins000.exe'
if (Test-Path $uninstaller -PathType Leaf) {
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    Assert-True ($uninstall.ExitCode -eq 0) "Uninstaller returned exit code $($uninstall.ExitCode)."
}

foreach ($desktop in $desktopCandidates) {
    Remove-Item (Join-Path $desktop $desktopShortcutName) -Force -ErrorAction SilentlyContinue
}
foreach ($programs in $programCandidates) {
    Remove-Item (Join-Path $programs $helpShortcutName) -Force -ErrorAction SilentlyContinue
}
Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue
