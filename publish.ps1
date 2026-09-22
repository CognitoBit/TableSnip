<#
.SYNOPSIS
  Builds a distributable copy of TableSnip into dist\TableSnip (plus a zip).

.PARAMETER SelfContained
  Bundle the .NET runtime so the app runs on PCs without .NET 8 installed (~70 MB larger).
  Without it, Windows offers to install the .NET 8 Desktop Runtime on first launch.
#>
param([switch]$SelfContained)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\TableSnip"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$publishArgs = @(
    "publish", (Join-Path $root "src\TableSnip\TableSnip.csproj"),
    "-c", "Release",
    "-r", "win-x64",
    "-o", $out,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" })
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$zip = Join-Path $root "dist\TableSnip-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

Write-Host ""
Write-Host "Published to $out"
Get-ChildItem $out -Recurse -File | ForEach-Object { "{0,10:N0} KB  {1}" -f ($_.Length / 1KB), $_.FullName.Substring($out.Length + 1) }
Write-Host ("Zip: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
