$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Front = Join-Path $Root 'frontend'
$Server = Join-Path $Root 'backend\TwinA.ControlServer'

function Assert-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Write-Host 'TWIN A - checking prerequisites...' -ForegroundColor Cyan
Assert-Command 'dotnet' 'Install the .NET 10 SDK and reopen PowerShell.'
Assert-Command 'node' 'Install Node.js 24 and reopen PowerShell.'
Assert-Command 'npm' 'npm is installed together with Node.js.'

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith('10.')) {
    throw ".NET 10 SDK is required. Detected: $dotnetVersion"
}

$nodeVersion = (& node --version).Trim()
Write-Host ".NET: $dotnetVersion | Node: $nodeVersion" -ForegroundColor DarkGray

& (Join-Path $PSScriptRoot 'detect-apps.ps1') -Quiet

Push-Location $Front
try {
    if (Test-Path 'package-lock.json') {
        npm ci
        Assert-LastExitCode 'npm ci'
    } else {
        npm install
        Assert-LastExitCode 'npm install'
    }

    npm run build
    Assert-LastExitCode 'Angular build'
}
finally { Pop-Location }

$Dist = Join-Path $Front 'dist\twina-control\browser'
$Www = Join-Path $Server 'wwwroot'
if (-not (Test-Path $Dist)) { throw "Angular output was not found at $Dist." }

New-Item -ItemType Directory -Force -Path $Www | Out-Null
Remove-Item "$Www\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$Dist\*" $Www -Recurse -Force

dotnet build "$Server\TwinA.ControlServer.csproj" -c Release
Assert-LastExitCode 'Control Server build'

dotnet build "$Root\backend\TwinA.DesktopAgent\TwinA.DesktopAgent.csproj" -c Release
Assert-LastExitCode 'Desktop Agent build'

dotnet build "$Root\backend\TwinA.Launcher\TwinA.Launcher.csproj" -c Release
Assert-LastExitCode 'Launcher build'

Write-Host 'TWIN A build complete - all build steps succeeded.' -ForegroundColor Green
