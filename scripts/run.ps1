$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot

$tailscaleDir = Join-Path $env:ProgramFiles 'Tailscale'
if (Test-Path $tailscaleDir) { $env:Path = "$tailscaleDir;$env:Path" }

foreach ($name in @('TWINA_OBS_PASSWORD','TWINA_MQTT_PASSWORD','TWINA_OBS_PATH')) {
    $stored = [Environment]::GetEnvironmentVariable($name, 'User')
    if (-not [string]::IsNullOrWhiteSpace($stored)) { Set-Item -Path "Env:$name" -Value $stored }
}

if ([string]::IsNullOrWhiteSpace($env:TWINA_OBS_PATH) -or -not (Test-Path $env:TWINA_OBS_PATH)) {
    & (Join-Path $PSScriptRoot 'detect-apps.ps1') -Quiet
}

$serverDll = Join-Path $Root 'backend\TwinA.ControlServer\bin\Release\net10.0\TwinA.ControlServer.dll'
$agentDll = Join-Path $Root 'backend\TwinA.DesktopAgent\bin\Release\net10.0-windows\TwinA.DesktopAgent.dll'
if (-not (Test-Path $serverDll) -or -not (Test-Path $agentDll)) {
    throw 'TWIN A has not been built yet. Run .\scripts\build.ps1 first.'
}

Write-Host 'Starting Desktop Agent...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList '-NoExit','-Command',"dotnet run --project `"$Root\backend\TwinA.DesktopAgent\TwinA.DesktopAgent.csproj`" -c Release --no-build"
Start-Sleep -Seconds 1
Write-Host 'Starting Control Server...' -ForegroundColor Cyan
dotnet run --project "$Root\backend\TwinA.ControlServer\TwinA.ControlServer.csproj" -c Release --no-build
