$ErrorActionPreference = 'SilentlyContinue'

$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\翻译助手.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AiTranslator'

Get-Process -Name AiTranslator -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch { }
}

Remove-Item -LiteralPath $shortcutPath -Force
Remove-Item -Path $uninstallKey -Recurse -Force

$cleanupPath = Join-Path $env:TEMP "AiTranslator-uninstall-$PID.cmd"
$cleanupContent = "@echo off`r`ntimeout /t 1 /nobreak >nul`r`nrmdir /s /q `"$installRoot`"`r`ndel `"%~f0`"`r`n"
Set-Content -LiteralPath $cleanupPath -Value $cleanupContent -Encoding ASCII
Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$cleanupPath`"" -WindowStyle Hidden
