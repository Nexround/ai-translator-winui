param(
    [Parameter(Mandatory = $true)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$stagingDirectory = Join-Path $repoRoot 'artifacts\installer-staging'
$sedPath = Join-Path $repoRoot 'artifacts\AiTranslator-Setup.sed'

if (-not (Test-Path -LiteralPath $PayloadDirectory)) {
    throw "Payload directory does not exist: $PayloadDirectory"
}

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $PayloadDirectory '*') -Destination $stagingDirectory -Recurse -Force
foreach ($scriptName in @('install.ps1', 'uninstall.ps1')) {
    $sourceScript = Join-Path $scriptRoot $scriptName
    $stagedScript = Join-Path $stagingDirectory $scriptName
    $scriptText = Get-Content -LiteralPath $sourceScript -Raw
    [System.IO.File]::WriteAllText($stagedScript, $scriptText, [System.Text.UTF8Encoding]::new($true))
}
Copy-Item -LiteralPath (Join-Path $scriptRoot 'install.cmd') -Destination $stagingDirectory -Force

$files = Get-ChildItem -LiteralPath $stagingDirectory -File | Sort-Object Name
$stringLines = [System.Collections.Generic.List[string]]::new()
$sourceLines = [System.Collections.Generic.List[string]]::new()
$sourceFileLines = [System.Collections.Generic.List[string]]::new()
$sourceLines.Add('[SourceFiles]')
$sourceLines.Add("SourceFiles0=$stagingDirectory")
$sourceLines.Add('')
$sourceFileLines.Add('[SourceFiles0]')

for ($index = 0; $index -lt $files.Count; $index++) {
    $name = "FILE$index"
    $stringLines.Add("$name=$($files[$index].Name)")
    $sourceFileLines.Add("%$name%=")
}

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=I
InstallPrompt=%InstallPrompt%
DisplayLicense=
FinishMessage=%FinishMessage%
TargetName=$OutputPath
FriendlyName=%FriendlyName%
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd
UserQuietInstCmd=install.cmd
SourceFiles=SourceFiles

[Strings]
InstallPrompt=正在安装翻译助手。
FinishMessage=翻译助手安装完成。
FriendlyName=翻译助手安装程序
$($stringLines -join "`r`n")

$($sourceLines -join "`r`n")
$($sourceFileLines -join "`r`n")
"@

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
Set-Content -LiteralPath $sedPath -Value $sed -Encoding Unicode
Start-Process -FilePath "$env:WINDIR\System32\iexpress.exe" -ArgumentList @('/N', '/Q', $sedPath) -Wait -NoNewWindow
$deadline = (Get-Date).AddSeconds(120)
while (-not (Test-Path -LiteralPath $OutputPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
}
if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw "IExpress failed to create installer: $OutputPath"
}
