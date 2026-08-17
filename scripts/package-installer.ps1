param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts\installer'
$Payload = Join-Path $Artifacts 'payload'

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

Write-Host 'Building TWIN A before packaging...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'build.ps1')

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Payload | Out-Null

$projects = @(
    @{ Name='server'; Project='backend\TwinA.ControlServer\TwinA.ControlServer.csproj' },
    @{ Name='agent'; Project='backend\TwinA.DesktopAgent\TwinA.DesktopAgent.csproj' },
    @{ Name='launcher'; Project='backend\TwinA.Launcher\TwinA.Launcher.csproj' }
)

foreach ($item in $projects) {
    $out = Join-Path $Payload $item.Name
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    dotnet publish (Join-Path $Root $item.Project) `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -o $out
    Assert-LastExitCode "Publish $($item.Name)"
}

Copy-Item (Join-Path $Root 'README.md') (Join-Path $Payload 'README.md') -Force
if (Test-Path (Join-Path $Root 'LICENSE')) { Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $Payload 'LICENSE') -Force }
Copy-Item (Join-Path $Root 'installer\install-dependencies.ps1') (Join-Path $Payload 'install-dependencies.ps1') -Force

$innoCandidates = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $innoCandidates) {
    throw 'Inno Setup compiler was not found. Install Inno Setup 7 (recommended) or 6 and run this script again.'
}

$iss = Join-Path $Root 'installer\TwinAControlCenter.iss'
& $innoCandidates[0] "/DSourceRoot=$Payload" "/DOutputRoot=$Artifacts" $iss
Assert-LastExitCode 'Inno Setup compile'

Write-Host "Installer created under $Artifacts" -ForegroundColor Green
