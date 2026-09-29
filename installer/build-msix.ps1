param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x86', 'x64', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$OutputDirectory = '',
    [switch]$Sign,
    [string]$CertificatePath = '',
    [string]$CertificatePassword = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'AiTranslator.WinUI.csproj'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts\msix' }
$runtime = switch ($Platform) {
    'x86' { 'win-x86' }
    'ARM64' { 'win-arm64' }
    default { 'win-x64' }
}

# The Windows SDK BuildTools NuGet package selects tools from this variable.
# PowerShell launched from a 32-bit host otherwise makes it choose x86 tools.
$env:PROCESSOR_ARCHITECTURE = if ($Platform -eq 'ARM64') { 'ARM64' } else { 'AMD64' }

# Let the MSIX targets locate mspdbcmf.exe and emit the optional symbols
# package when Visual Studio Build Tools is installed outside a dev prompt.
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vsWhere) {
    $vsInstall = (& $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath).Trim()
    if ($vsInstall -and (Test-Path $vsInstall)) {
        $env:VsInstallRoot = $vsInstall
        $vcRoot = Join-Path $vsInstall 'VC\Tools\MSVC'
        $vcVersion = Get-ChildItem $vcRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if ($vcVersion) { $env:VCToolsInstallDir = "$($vcVersion.FullName)\" }
    }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:RuntimeIdentifier=$runtime `
    -p:EnableMsixTooling=true `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "MSIX publish failed with exit code $LASTEXITCODE." }

$package = Get-ChildItem (Join-Path $root 'AppPackages') -Filter '*.msix' -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) { throw 'MSIX publish completed without producing an .msix package.' }

$finalPackage = Join-Path $output $package.Name
Copy-Item $package.FullName $finalPackage -Force

if ($Sign) {
    if (-not $CertificatePath -or -not (Test-Path $CertificatePath)) {
        throw 'Signing requires -CertificatePath pointing to a .pfx certificate.'
    }

    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64'
    $signTool = Join-Path $sdkBin 'signtool.exe'
    if (-not (Test-Path $signTool)) { throw "signtool.exe was not found at $signTool." }
    & $signTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $finalPackage
    if ($LASTEXITCODE -ne 0) { throw "MSIX signing failed with exit code $LASTEXITCODE." }
}

Write-Host "MSIX package: $finalPackage"
