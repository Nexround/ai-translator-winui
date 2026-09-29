$ErrorActionPreference = 'Stop'

$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\AiTranslator'
$startMenuRoot = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuRoot '翻译助手.lnk'
$uninstallPath = Join-Path $installRoot 'uninstall.ps1'
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-Process -Name AiTranslator -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        # A running instance from another location does not belong to this install.
    }
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Force |
    Where-Object { $_.Name -notin @('install.ps1', 'install.cmd') } |
    Copy-Item -Destination $installRoot -Recurse -Force

New-Item -ItemType Directory -Path $startMenuRoot -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installRoot 'AiTranslator.exe'
$shortcut.WorkingDirectory = $installRoot
$shortcut.IconLocation = "$(Join-Path $installRoot 'AiTranslator.exe'),0"
$shortcut.Description = '翻译助手'
$shortcut.Save()

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AiTranslator'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value '翻译助手' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Nexround' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value (Join-Path $installRoot 'AiTranslator.exe') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPath`"" -PropertyType String -Force | Out-Null

Start-Process -FilePath (Join-Path $installRoot 'AiTranslator.exe') -WorkingDirectory $installRoot
