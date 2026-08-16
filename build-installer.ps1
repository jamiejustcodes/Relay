# =====================================================================
# Relay Installer & Security Obfuscation Packaging Pipeline
# =====================================================================
param(
    [ValidateSet("FrameworkDependent", "SelfContained")]
    [string]$DeploymentMode = "FrameworkDependent"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Relay Hardened Packaging Pipeline ($DeploymentMode)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

# 1. Locate Inno Setup Compiler (ISCC.exe)
$IsccPaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
)

$IsccExe = $IsccPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $IsccExe) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) was not found in standard paths."
    Write-Host "Attempting to install Inno Setup via winget..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup --exact --silent --accept-source-agreements --accept-package-agreements
    
    $IsccExe = $IsccPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $IsccExe) {
        throw "Could not locate ISCC.exe. Please install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
    }
}

Write-Host "[1/5] Found Inno Setup Compiler: $IsccExe" -ForegroundColor Green

# 2. Locate / Install Obfuscar Global Tool
$ObfuscarCmd = Get-Command obfuscar.console -ErrorAction SilentlyContinue
if (-not $ObfuscarCmd) {
    $UserToolPath = "$env:USERPROFILE\.dotnet\tools\obfuscar.console.exe"
    if (Test-Path $UserToolPath) {
        $ObfuscarExe = $UserToolPath
    } else {
        Write-Host "Installing Obfuscar Global Tool..." -ForegroundColor Yellow
        dotnet tool install --global Obfuscar.GlobalTool
        $ObfuscarExe = "$env:USERPROFILE\.dotnet\tools\obfuscar.console.exe"
    }
} else {
    $ObfuscarExe = $ObfuscarCmd.Source
}

Write-Host "[2/5] Found Obfuscar Tool: $ObfuscarExe" -ForegroundColor Green

# 3. Build and Publish Relay
$PublishDir = Join-Path $RepoRoot "bin\publish\win-x64"
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

$IsSelfContained = ($DeploymentMode -eq "SelfContained")
Write-Host "[3/5] Publishing Relay ($DeploymentMode, Release, win-x64)..." -ForegroundColor Yellow

& dotnet publish "$RepoRoot\src\Relay.UI\Relay.UI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained $IsSelfContained `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 4. Optimization & Stripping for Self-Contained mode
if ($IsSelfContained) {
    Write-Host "  -> Stripping unused diagnostic DACs, symbol readers, and legacy modules..." -ForegroundColor Yellow
    
    # Remove debugger and diagnostic native DAC engines not needed at runtime
    $DiagnosticFiles = @(
        "mscordbi.dll",
        "Microsoft.DiaSymReader.Native.amd64.dll",
        "ReachFramework.dll",
        "System.Windows.Controls.Ribbon.dll",
        "PresentationUI.dll",
        "Microsoft.VisualBasic.Core.dll"
    )
    foreach ($file in $DiagnosticFiles) {
        $filePath = Join-Path $PublishDir $file
        if (Test-Path $filePath) { Remove-Item $filePath -Force -ErrorAction SilentlyContinue }
    }
    Get-ChildItem -Path $PublishDir -Filter "mscordaccore*.dll" | Remove-Item -Force -ErrorAction SilentlyContinue

    # Remove non-English satellite resource folders
    $SatelliteFolders = @("cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant")
    foreach ($lang in $SatelliteFolders) {
        $langDir = Join-Path $PublishDir $lang
        if (Test-Path $langDir) { Remove-Item $langDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# 5. Perform MSIL Obfuscation on Core and Infrastructure
Write-Host "[4/5] Generating Obfuscar Configuration and Executing Obfuscation..." -ForegroundColor Yellow
$ObfuscarConfigFile = Join-Path $RepoRoot "installer\obfuscar.xml"

$ObfuscarXmlContent = @"
<?xml version="1.0" encoding="utf-8" ?>
<Obfuscator>
  <Var name="InPath" value="$PublishDir" />
  <Var name="OutPath" value="$PublishDir\obfuscated" />
  
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="RenameProperties" value="false" />
  <Var name="RenameEvents" value="true" />
  <Var name="RenameFields" value="true" />
  <Var name="UseUnicodeNames" value="true" />
  <Var name="HideStrings" value="true" />
  <Var name="OptimizeMethods" value="true" />
  <Var name="SuppressIldasm" value="true" />

  <!-- Core Layer Obfuscation -->
  <Module file="`$(InPath)\Relay.Core.dll">
    <SkipType name="Relay.Core.Models.*" />
    <SkipType name="Relay.Core.Interfaces.*" />
  </Module>

  <!-- Infrastructure Layer Obfuscation -->
  <Module file="`$(InPath)\Relay.Infrastructure.dll">
    <SkipType name="Relay.Infrastructure.Data.*" />
    <SkipType name="Relay.Infrastructure.Security.*" />
    <SkipType name="Relay.Infrastructure.ScreenCapture.NativeMethods" />
    <SkipType name="Relay.Infrastructure.ScreenCapture.NativeMethods/*" />
  </Module>
</Obfuscator>
"@

Set-Content -Path $ObfuscarConfigFile -Value $ObfuscarXmlContent -Encoding UTF8

& "$ObfuscarExe" "$ObfuscarConfigFile"

if ($LASTEXITCODE -ne 0) {
    throw "Obfuscar obfuscation failed with exit code $LASTEXITCODE"
}

# Replace original DLLs with obfuscated DLLs
$ObfuscatedDir = Join-Path $PublishDir "obfuscated"
if (Test-Path $ObfuscatedDir) {
    Get-ChildItem -Path $ObfuscatedDir -Filter "*.dll" | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $PublishDir -Force
    }
    Remove-Item -Path $ObfuscatedDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Strip any stray PDBs or symbol mappings
Get-ChildItem -Path $PublishDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
$MappingFile = Join-Path $RepoRoot "Mapping.txt"
if (Test-Path $MappingFile) { Remove-Item $MappingFile -Force }

# Measure installed footprint
$InstalledBytes = (Get-ChildItem -Path $PublishDir -Recurse | Measure-Object -Property Length -Sum).Sum
$InstalledMB = [math]::Round(($InstalledBytes / 1MB), 2)
$FileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count

Write-Host "  -> Code obfuscation & symbol stripping completed successfully." -ForegroundColor Green
Write-Host "  -> Installed Directory Footprint: $InstalledMB MB ($FileCount files)" -ForegroundColor Cyan

# 6. Compile Installer with Inno Setup
$DistDir = Join-Path $RepoRoot "dist"
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

$IssFile = Join-Path $RepoRoot "installer\RelaySetup.iss"
Write-Host "[5/5] Compiling Inno Setup installer: $IssFile..." -ForegroundColor Yellow

$InnoArgs = @("$IssFile")
if ($DeploymentMode -eq "FrameworkDependent") {
    $InnoArgs += "/DFrameworkDependent=1"
} else {
    $InnoArgs += "/DSelfContained=1"
}

& "$IsccExe" $InnoArgs

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$SetupExe = Join-Path $DistDir "RelaySetup.exe"
if (Test-Path $SetupExe) {
    $SetupSizeMB = [math]::Round(((Get-Item $SetupExe).Length / 1MB), 2)
    $BaselineInstalledMB = 175.0
    $ReductionPercent = [math]::Round(((1 - ($InstalledMB / $BaselineInstalledMB)) * 100), 1)

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  SUCCESS! Hardened Installer generated ($DeploymentMode):" -ForegroundColor Green
    Write-Host "  Installer EXE : $SetupExe ($SetupSizeMB MB)" -ForegroundColor Cyan
    Write-Host "  Installed Size: $InstalledMB MB ($ReductionPercent% reduction vs 175 MB baseline!)" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    throw "Expected installer file was not found at $SetupExe"
}
