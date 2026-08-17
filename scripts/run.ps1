$Root = Split-Path -Parent $PSScriptRoot

# Ensure the Tailscale CLI is visible to the Control Server even when Rider/PowerShell
# was opened before Tailscale was installed or its installer did not update PATH.
$tailscaleDir = Join-Path $env:ProgramFiles 'Tailscale'
if (Test-Path $tailscaleDir) {
    $env:Path = "$tailscaleDir;$env:Path"
}
$storedObsPassword = [Environment]::GetEnvironmentVariable('TWINA_OBS_PASSWORD', 'User')
if (-not [string]::IsNullOrWhiteSpace($storedObsPassword)) {
    $env:TWINA_OBS_PASSWORD = $storedObsPassword
}

$storedMqttPassword = [Environment]::GetEnvironmentVariable('TWINA_MQTT_PASSWORD', 'User')
if (-not [string]::IsNullOrWhiteSpace($storedMqttPassword)) {
    $env:TWINA_MQTT_PASSWORD = $storedMqttPassword
}

Write-Host 'Starting Desktop Agent...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList '-NoExit','-Command',"dotnet run --project `"$Root\backend\TwinA.DesktopAgent\TwinA.DesktopAgent.csproj`" -c Release --no-build"
Start-Sleep -Seconds 1
Write-Host 'Starting Control Server...' -ForegroundColor Cyan
dotnet run --project "$Root\backend\TwinA.ControlServer\TwinA.ControlServer.csproj" -c Release --no-build
