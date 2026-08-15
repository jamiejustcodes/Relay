# =====================================================================
# Relay Installer Builder Script
# =====================================================================
$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Relay Installer Packaging Pipeline   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

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

Write-Host "[1/3] Found Inno Setup Compiler: $IsccExe" -ForegroundColor Green

# 2. Build and Publish Self-Contained Relay
$PublishDir = Join-Path $RepoRoot "bin\publish\win-x64"
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "[2/3] Publishing Relay (Release, self-contained, win-x64, ReadyToRun)..." -ForegroundColor Yellow

& dotnet publish "$RepoRoot\src\Relay.UI\Relay.UI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "  -> Published successfully to $PublishDir" -ForegroundColor Green

# 3. Compile Installer with Inno Setup
$DistDir = Join-Path $RepoRoot "dist"
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

$IssFile = Join-Path $RepoRoot "installer\RelaySetup.iss"
Write-Host "[3/3] Compiling Inno Setup script: $IssFile..." -ForegroundColor Yellow

& "$IsccExe" "$IssFile"

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$SetupExe = Join-Path $DistDir "RelaySetup.exe"
if (Test-Path $SetupExe) {
    $FileSizeMB = [math]::Round(((Get-Item $SetupExe).Length / 1MB), 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  SUCCESS! Installer generated:" -ForegroundColor Green
    Write-Host "  $SetupExe ($FileSizeMB MB)" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    throw "Expected installer file was not found at $SetupExe"
}
