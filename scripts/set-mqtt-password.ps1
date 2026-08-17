$ErrorActionPreference = 'Stop'
Write-Host 'TWIN A - MQTT password setup' -ForegroundColor Cyan
$secure = Read-Host 'Enter the MQTT password (leave blank to clear)' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
[Environment]::SetEnvironmentVariable('TWINA_MQTT_PASSWORD', $plain, 'User')
if ([string]::IsNullOrEmpty($plain)) { Write-Host 'TWINA_MQTT_PASSWORD cleared.' -ForegroundColor Yellow } else { Write-Host 'MQTT password stored in your Windows user environment. Restart TWIN A to load it.' -ForegroundColor Green }
