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

# The target architecture and the host architecture are different concepts.
# ARM64 packages are commonly cross-built on an x64 GitHub runner, so selecting
# ARM64 SDK executables here would try to execute ARM64 mt.exe on x64 Windows.
# Configure the SDK package from the detected host architecture. This is a
# host-tool selection, not a target-platform selection.
$processArchitecture = if ($env:PROCESSOR_ARCHITEW6432) {
    $env:PROCESSOR_ARCHITEW6432
} else {
    $env:PROCESSOR_ARCHITECTURE
}
$processArchitecture = if ($processArchitecture) {
    $processArchitecture
} else {
    [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
}
$hostToolArchitecture = switch ($processArchitecture.ToUpperInvariant()) {
    'AMD64' { 'x64' }
    'X64' { 'x64' }
    'ARM64' { 'arm64' }
    'X86' { 'x86' }
    default { throw "Unsupported host architecture '$processArchitecture'." }
}
$sdkEnvironmentArchitecture = switch ($hostToolArchitecture) {
    'x64' { 'AMD64' }
    'arm64' { 'ARM64' }
    'x86' { 'x86' }
}
$env:PROCESSOR_ARCHITECTURE = $sdkEnvironmentArchitecture

Write-Host "Target platform: $Platform ($runtime)"
Write-Host "Host architecture: $processArchitecture; Windows SDK tools: $hostToolArchitecture"

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

$publishArguments = @(
    $project
    '-c', $Configuration
    "-p:Platform=$Platform"
    "-p:RuntimeIdentifier=$runtime"
    '-p:EnableMsixTooling=true'
    '-p:GenerateAppxPackageOnBuild=true'
    '-p:AppxPackageSigningEnabled=false'
    '-p:WindowsAppSDKSelfContained=true'
    '-p:SelfContained=true'
    '-o', $output
)

& dotnet publish @publishArguments
if ($LASTEXITCODE -ne 0) { throw "MSIX publish failed with exit code $LASTEXITCODE." }

$package = Get-ChildItem (Join-Path $root 'AppPackages') -Filter "*_$Platform.msix" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) { throw "MSIX publish completed without producing a $Platform .msix package." }

$finalPackage = Join-Path $output $package.Name
Copy-Item $package.FullName $finalPackage -Force
Copy-Item (Join-Path $PSScriptRoot 'install-msix.ps1') (Join-Path $output 'install-msix.ps1') -Force

if ($Sign) {
    if (-not $CertificatePath -or -not (Test-Path $CertificatePath)) {
        throw 'Signing requires -CertificatePath pointing to a .pfx certificate.'
    }

    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64'
    $signTool = Join-Path $sdkBin 'signtool.exe'
    if (-not (Test-Path $signTool)) { throw "signtool.exe was not found at $signTool." }
    & $signTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $finalPackage
    if ($LASTEXITCODE -ne 0) { throw "MSIX signing failed with exit code $LASTEXITCODE." }

    # Ship only the public certificate beside the package. The private PFX is
    # intentionally never copied into the distributable output.
    $publicCertificatePath = Join-Path $output 'AiTranslator-DevCert.cer'
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath, $CertificatePassword)
    Export-Certificate -Cert $certificate -FilePath $publicCertificatePath | Out-Null
}

Write-Host "MSIX package: $finalPackage"
