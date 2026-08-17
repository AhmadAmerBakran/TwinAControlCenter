$ErrorActionPreference = 'Stop'

$tailscale = Get-Command tailscale -ErrorAction SilentlyContinue
if (-not $tailscale) {
    $fallback = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
    if (Test-Path $fallback) { $tailscalePath = $fallback }
    else { throw 'Tailscale CLI was not found. Install Tailscale and sign in first.' }
} else {
    $tailscalePath = $tailscale.Source
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    throw 'Tailscale Serve setup must be run from PowerShell opened with Run as administrator.'
}

Write-Host 'Publishing localhost:5055 privately to your tailnet...' -ForegroundColor Cyan
& $tailscalePath serve --bg 5055
if ($LASTEXITCODE -ne 0) { throw "tailscale serve failed with exit code $LASTEXITCODE." }

& $tailscalePath serve status
if ($LASTEXITCODE -ne 0) { throw "tailscale serve status failed with exit code $LASTEXITCODE." }
