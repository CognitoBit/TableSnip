<#
.SYNOPSIS
  Builds TableSnip distributables into dist\.

  Always:        dist\TableSnip-<version>-win-x64.zip   portable build (needs the .NET 8 Desktop Runtime)
  With -Installer: dist\TableSnip-Setup-<version>.exe   self-contained installer, no prerequisites

.PARAMETER Installer
  Also build the installer. Needs Inno Setup 6:  winget install JRSoftware.InnoSetup

.PARAMETER SelfContained
  Make the portable zip self-contained too (about 70 MB larger). The installer always is.

.PARAMETER Version
  Version stamp for the binaries, zip and installer. Defaults to <Version> in the csproj.
#>
param(
    [switch]$Installer,
    [switch]$SelfContained,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\TableSnip\TableSnip.csproj"

if (-not $Version) {
    $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { $Version = "1.0.0" }

function Copy-VcRuntime([string]$outDir) {
    # Tesseract's native DLLs need the VC++ 2015-2022 runtime. Ship it app-local when a Visual
    # Studio redist folder is available (it is on GitHub's Windows runners). Otherwise the
    # installer downloads it from Microsoft on PCs that lack it.
    $crt = @("${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio") |
        Where-Object { $_ -and (Test-Path $_) } |
        ForEach-Object { Get-Item "$_\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC14*.CRT" -ErrorAction SilentlyContinue } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($crt) {
        foreach ($n in "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll") {
            Copy-Item (Join-Path $crt.FullName $n) $outDir -Force
        }
        Write-Host "VC++ runtime bundled from $($crt.FullName)"
    } else {
        Write-Warning "No Visual Studio redist folder found; VC++ runtime not bundled (the installer downloads it when a PC lacks it)."
    }
}

function Publish-App([string]$outDir, [bool]$selfContained) {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    $sc = if ($selfContained) { "true" } else { "false" }
    & dotnet publish $proj -c Release -r win-x64 -o $outDir --self-contained $sc `
        "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" "-p:DebugType=none" "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Copy-VcRuntime $outDir
}

New-Item -ItemType Directory -Force (Join-Path $root "dist") | Out-Null

# ---- 1. portable zip
$portable = Join-Path $root "dist\TableSnip"
Publish-App $portable $SelfContained.IsPresent
$zip = Join-Path $root "dist\TableSnip-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath $zip
Write-Host ("Portable zip: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# ---- 2. installer
if ($Installer) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup" }

    $sc = Join-Path $root "dist\TableSnip-selfcontained"
    Publish-App $sc $true

    & $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$sc" (Join-Path $root "installer\TableSnip.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    $setup = Join-Path $root "dist\TableSnip-Setup-$Version.exe"
    Write-Host ("Installer:    {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
}
