Write-Host 'TWIN A - OBS WebSocket password setup' -ForegroundColor Cyan
$secure = Read-Host 'Enter the OBS WebSocket password (it will not be displayed)' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    if ([string]::IsNullOrWhiteSpace($plain)) { throw 'Password cannot be empty.' }
    [Environment]::SetEnvironmentVariable('TWINA_OBS_PASSWORD', $plain, 'User')
    Write-Host 'OBS password saved to the current Windows user environment.' -ForegroundColor Green
    Write-Host 'The TWIN A run script will load it without putting the password in source code.' -ForegroundColor DarkGray
}
finally {
    if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    $plain = $null
}
