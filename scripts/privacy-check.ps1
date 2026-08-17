$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required for the repository privacy check.'
    }

    $binaryExtensions = @(
        '.png','.jpg','.jpeg','.gif','.webp','.ico','.pdf','.zip','.7z','.exe','.dll','.pdb','.woff','.woff2','.ttf','.otf'
    )

    $allowedIpv4 = @('127.0.0.1','0.0.0.0','255.255.255.255')
    $findings = New-Object System.Collections.Generic.List[string]
    $tracked = git ls-files

    foreach ($relative in $tracked) {
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if ($binaryExtensions -contains $extension) { continue }
        $full = Join-Path $Root $relative
        if (-not (Test-Path $full -PathType Leaf)) { continue }

        $text = Get-Content $full -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrEmpty($text)) { continue }

        if ($text -match '(?i)C:\\Users\\[A-Za-z0-9._-]+') {
            $findings.Add("$relative contains a hard-coded Windows user-profile path.")
        }

        foreach ($match in [regex]::Matches($text, '(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])')) {
            $ip = $match.Value
            if ($allowedIpv4 -notcontains $ip) {
                $findings.Add("$relative contains a literal IPv4 address ($ip). Replace machine-specific addresses with discovery/configuration.")
            }
        }

        $secretPatterns = @(
            '(?i)github_pat_[A-Za-z0-9_]{20,}',
            '(?i)gh[pousr]_[A-Za-z0-9]{20,}',
            '(?i)sk-[A-Za-z0-9_-]{20,}',
            'AKIA[0-9A-Z]{16}',
            'AIza[0-9A-Za-z_-]{30,}',
            '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        )
        foreach ($pattern in $secretPatterns) {
            if ($text -match $pattern) {
                $findings.Add("$relative matches a credential/private-key pattern.")
                break
            }
        }

        if ($relative -match '(?i)appsettings.*\.json$' -and $text -match '(?i)"Password"\s*:\s*"[^"\s]+"') {
            $findings.Add("$relative contains a non-empty Password value.")
        }
    }

    if ($findings.Count -gt 0) {
        Write-Host 'TWIN A privacy check FAILED:' -ForegroundColor Red
        $findings | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
        throw 'Remove machine-specific/personal/sensitive data before publishing.'
    }

    Write-Host 'TWIN A privacy check passed: no hard-coded user paths, unexpected literal IPv4 addresses, or common credential patterns were found in tracked text files.' -ForegroundColor Green
}
finally {
    Pop-Location
}
