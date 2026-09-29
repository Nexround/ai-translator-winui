param(
    [string]$PackagePath = '',
    [string]$CertificatePath = '',
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $PackagePath) {
    $PackagePath = (Get-ChildItem (Join-Path $root 'artifacts') -Filter '*.msix' -Recurse |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $PackagePath -or -not (Test-Path $PackagePath)) {
    throw '找不到 MSIX。请使用 -PackagePath 指定 .msix 文件。'
}
$PackagePath = (Resolve-Path $PackagePath).Path

if (-not $CertificatePath) {
    $CertificatePath = Join-Path (Split-Path $PackagePath) 'AiTranslator-DevCert.cer'
}
if (-not (Test-Path $CertificatePath)) {
    throw '找不到签名证书。请将签名包旁的 AiTranslator-DevCert.cer 一并保留。'
}
$CertificatePath = (Resolve-Path $CertificatePath).Path

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $Elevated) {
    # AppX validates a development root with the system trust provider. Ask for
    # one explicit UAC elevation, then return the elevated process exit code.
    $child = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-PackagePath', $PackagePath, '-CertificatePath', $CertificatePath, '-Elevated')
    exit $child.ExitCode
}

Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown
Write-Host "MSIX 已安装：$PackagePath"
