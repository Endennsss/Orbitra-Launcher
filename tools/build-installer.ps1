param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "bin/installer-csharp"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$aotDirectory = Join-Path $repository "bin/installer-aot"
$bundleDirectory = Join-Path $repository "bin/installer-bundle"
$finalDirectory = Join-Path $repository $OutputDirectory
$payload = Join-Path $bundleDirectory "payload.zip"

dotnet publish (Join-Path $repository "Orbitra.Installer/Orbitra.Installer.csproj") `
    -c $Configuration -r win-x64 --self-contained true --nologo -o $aotDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $finalDirectory -Force | Out-Null
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }

$payloadFiles = Get-ChildItem -LiteralPath $aotDirectory -File | Where-Object Extension -ne ".pdb"
Compress-Archive -LiteralPath $payloadFiles.FullName -DestinationPath $payload -CompressionLevel Optimal

dotnet publish (Join-Path $repository "Orbitra.Installer.Bundle/Orbitra.Installer.Bundle.csproj") `
    -c $Configuration -r win-x64 --self-contained true --nologo -o $finalDirectory `
    "/p:InstallerPayload=$payload"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pdb = Join-Path $finalDirectory "Orbitra_Launcher_Installer.pdb"
if (Test-Path -LiteralPath $pdb) { Remove-Item -LiteralPath $pdb -Force }

Write-Host "Orbitra installer: $(Join-Path $finalDirectory 'Orbitra_Launcher_Installer.exe')"
