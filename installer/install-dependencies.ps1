param(
    [switch]$Tailscale,
    [switch]$Obs,
    [switch]$Steam,
    [switch]$Discord
)

$ErrorActionPreference = 'Continue'
$logRoot = Join-Path $env:LOCALAPPDATA 'TwinAControlCenter\Installer'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$logFile = Join-Path $logRoot 'dependencies.log'

function Write-Log([string]$Message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Message"
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Test-PackageInstalled([string]$Id) {
    $output = winget list --id $Id -e --accept-source-agreements --disable-interactivity 2>$null | Out-String
    return ($LASTEXITCODE -eq 0 -and $output -match [regex]::Escape($Id))
}

function Install-Package([string]$Id, [string]$Name) {
    if (Test-PackageInstalled $Id) {
        Write-Log "$Name already installed. Skipped."
        return
    }

    Write-Log "Installing $Name ($Id) with Windows Package Manager."
    winget install --id $Id -e --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -eq 0 -and (Test-PackageInstalled $Id)) {
        Write-Log "$Name installed and detected successfully."
    } elseif ($LASTEXITCODE -eq 0) {
        Write-Log "$Name installer returned success but the package could not yet be detected. The user should verify the application before relying on the integration."
    } else {
        Write-Log "$Name installation returned exit code $LASTEXITCODE. The user can install it manually later."
    }
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Log 'Windows Package Manager (winget) is not available. Optional dependency installation was skipped.'
    Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue
    [System.Windows.MessageBox]::Show(
        'Windows Package Manager is not available on this PC, so selected companion applications could not be installed automatically. TWIN A itself is installed. Install Tailscale, OBS Studio, Steam, or Discord manually if you need those integrations.',
        'TWIN A Control Center'
    ) | Out-Null
    exit 0
}

if ($Tailscale) { Install-Package 'Tailscale.Tailscale' 'Tailscale' }
if ($Obs)       { Install-Package 'OBSProject.OBSStudio' 'OBS Studio' }
if ($Steam)     { Install-Package 'Valve.Steam' 'Steam' }
if ($Discord)   { Install-Package 'Discord.Discord' 'Discord' }

Write-Log 'Dependency selection finished.'
exit 0
