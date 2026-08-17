$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Front = Join-Path $Root 'frontend'
$Server = Join-Path $Root 'backend\TwinA.ControlServer'

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Push-Location $Front
try {
    if (-not (Test-Path 'node_modules')) { npm install; Assert-LastExitCode 'npm install' }
    npm run build; Assert-LastExitCode 'Angular build'
}
finally { Pop-Location }

$Dist = Join-Path $Front 'dist\twina-control\browser'
$Www = Join-Path $Server 'wwwroot'
New-Item -ItemType Directory -Force -Path $Www | Out-Null
Remove-Item "$Www\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$Dist\*" $Www -Recurse -Force

dotnet build "$Server\TwinA.ControlServer.csproj" -c Release; Assert-LastExitCode 'Control Server build'
dotnet build "$Root\backend\TwinA.DesktopAgent\TwinA.DesktopAgent.csproj" -c Release; Assert-LastExitCode 'Desktop Agent build'
Write-Host 'TWIN A build complete - all build steps succeeded.' -ForegroundColor Cyan
