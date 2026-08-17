$ErrorActionPreference = 'Stop'
$Base = 'http://127.0.0.1:5055'

function Test-Endpoint([string]$Name, [string]$Path) {
    try {
        $result = Invoke-RestMethod -Uri "$Base$Path" -Method Get -TimeoutSec 10
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return $result
    }
    catch {
        Write-Host "[FAIL] $Name - $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

Write-Host "TWIN A v0.5 read-only smoke test" -ForegroundColor Cyan
Write-Host "Server: $Base" -ForegroundColor DarkGray

$health = Test-Endpoint 'Health' '/api/health'
$state = Test-Endpoint 'Live state' '/api/state'
$system = Test-Endpoint 'System details' '/api/system/details'
$audio = Test-Endpoint 'Audio endpoints' '/api/audio/devices'
$games = Test-Endpoint 'Game library discovery' '/api/games'
$drives = Test-Endpoint 'Drive discovery' '/api/files/drives'
$projects = Test-Endpoint 'Developer projects' '/api/dev/projects'
$flows = Test-Endpoint 'Flows' '/api/flows'
$settings = Test-Endpoint 'Settings' '/api/settings'
$iot = Test-Endpoint 'IoT state endpoint' '/api/iot/states'

Write-Host "`nSummary" -ForegroundColor Cyan
Write-Host "Version:          $($health.version)"
Write-Host "Desktop Agent:    $($state.agent)"
Write-Host "OBS:              $($state.obs)"
Write-Host "Tailscale:        $($state.vpn)"
Write-Host "Network adapter:  $($system.network.name) - $($system.network.linkSpeed)"
Write-Host "Audio endpoints:  $(@($audio).Count)"
Write-Host "Games discovered: $(@($games).Count)"
Write-Host "Drives:           $(@($drives).Count)"
Write-Host "Dev projects:     $(@($projects).Count)"
Write-Host "Flows:            $(@($flows).Count)"
Write-Host "IoT devices:      $(@($iot).Count)"

if ($state.agent -ne 'online') { Write-Host '[WARN] Desktop Agent is not online.' -ForegroundColor Yellow }
if ($state.obs -ne 'ready') { Write-Host '[WARN] OBS is not ready. This is fine if OBS is closed.' -ForegroundColor Yellow }
if ($system.network.name -eq '—') { Write-Host '[WARN] No primary physical network adapter was selected.' -ForegroundColor Yellow }

Write-Host "`nSmoke test completed without mutating Windows, files, OBS, games, or IoT devices." -ForegroundColor Green
