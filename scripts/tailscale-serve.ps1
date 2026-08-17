$ErrorActionPreference = 'Stop'
Write-Host 'Publishing localhost:5055 privately to your tailnet...' -ForegroundColor Cyan
tailscale serve --bg 5055
tailscale serve status
