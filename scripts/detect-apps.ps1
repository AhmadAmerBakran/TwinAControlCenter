param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Write-Info([string]$Message) {
    if (-not $Quiet) { Write-Host $Message -ForegroundColor Cyan }
}

function Add-Candidate([System.Collections.Generic.List[string]]$List, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not $List.Contains($expanded)) { $List.Add($expanded) }
}

function Get-SteamRoots {
    $roots = New-Object 'System.Collections.Generic.List[string]'
    $registryPaths = @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )

    foreach ($key in $registryPaths) {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($property in @('SteamPath','InstallPath')) {
                $value = $item.$property
                if (-not [string]::IsNullOrWhiteSpace($value) -and -not $roots.Contains($value)) { $roots.Add($value) }
            }
        } catch { }
    }

    foreach ($fallback in @("$env:ProgramFiles(x86)\Steam", "$env:ProgramFiles\Steam")) {
        if (-not [string]::IsNullOrWhiteSpace($fallback) -and -not $roots.Contains($fallback)) { $roots.Add($fallback) }
    }
    return $roots
}

function Get-SteamLibraries([string]$SteamRoot) {
    $libraries = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) { $libraries.Add($SteamRoot) }
    $vdf = Join-Path $SteamRoot 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        try {
            $text = Get-Content -LiteralPath $vdf -Raw
            foreach ($match in [regex]::Matches($text, '"path"\s+"(?<path>[^"]+)"', 'IgnoreCase')) {
                $library = $match.Groups['path'].Value -replace '\\\\','\'
                if (-not [string]::IsNullOrWhiteSpace($library) -and -not $libraries.Contains($library)) { $libraries.Add($library) }
            }
        } catch { }
    }
    return $libraries
}

Write-Info 'TWIN A - detecting optional applications...'

$obsCandidates = New-Object 'System.Collections.Generic.List[string]'
Add-Candidate $obsCandidates "$env:ProgramFiles\obs-studio\bin\64bit\obs64.exe"
Add-Candidate $obsCandidates "$env:ProgramFiles(x86)\obs-studio\bin\64bit\obs64.exe"
Add-Candidate $obsCandidates "$env:LOCALAPPDATA\Programs\obs-studio\bin\64bit\obs64.exe"

foreach ($steamRoot in Get-SteamRoots) {
    foreach ($library in Get-SteamLibraries $steamRoot) {
        Add-Candidate $obsCandidates (Join-Path $library 'steamapps\common\OBS Studio\bin\64bit\obs64.exe')
    }
}

$currentObs = [Environment]::GetEnvironmentVariable('TWINA_OBS_PATH', 'User')
if (-not [string]::IsNullOrWhiteSpace($currentObs) -and (Test-Path ([Environment]::ExpandEnvironmentVariables($currentObs)))) {
    $obsPath = [Environment]::ExpandEnvironmentVariables($currentObs)
} else {
    $obsPath = $obsCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($obsPath) {
    [Environment]::SetEnvironmentVariable('TWINA_OBS_PATH', $obsPath, 'User')
    $env:TWINA_OBS_PATH = $obsPath
    if (-not $Quiet) { Write-Host "OBS found: $obsPath" -ForegroundColor Green }
} else {
    [Environment]::SetEnvironmentVariable('TWINA_OBS_PATH', $null, 'User')
    Remove-Item Env:TWINA_OBS_PATH -ErrorAction SilentlyContinue
    if (-not $Quiet) {
        Write-Host 'OBS Studio was not found. This is fine if you do not use OBS.' -ForegroundColor Yellow
        Write-Host 'For a portable/custom OBS installation, set TWINA_OBS_PATH manually:' -ForegroundColor DarkGray
        Write-Host '[Environment]::SetEnvironmentVariable("TWINA_OBS_PATH", "C:\path\to\obs64.exe", "User")' -ForegroundColor DarkGray
    }
}

if (-not $Quiet) { Write-Host 'Application detection complete.' -ForegroundColor Cyan }
