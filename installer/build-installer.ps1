# TGD Soundboard Installer Build Script
# Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

Write-Host "=== TGD Soundboard Installer Build ===" -ForegroundColor Cyan
Write-Host ""

# Check for Inno Setup
if (-not (Test-Path $InnoSetupPath)) {
    # Try alternate locations
    $alternatePaths = @(
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )

    $found = $false
    foreach ($path in $alternatePaths) {
        if (Test-Path $path) {
            $InnoSetupPath = $path
            $found = $true
            break
        }
    }

    if (-not $found) {
        Write-Host "ERROR: Inno Setup not found!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please install Inno Setup 6 from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

Write-Host "Using Inno Setup: $InnoSetupPath" -ForegroundColor Green

# Navigate to project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "Project root: $projectRoot"

# Step 1: Build and publish the application
Write-Host ""
Write-Host "Step 1: Publishing application..." -ForegroundColor Yellow

Push-Location $projectRoot
try {
    dotnet publish src/TgdSoundboard/TgdSoundboard.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish application"
    }
    Write-Host "Application published successfully" -ForegroundColor Green
}
finally {
    Pop-Location
}

# Step 2: Check VB-Cable files
Write-Host ""
Write-Host "Step 2: Checking VB-Cable files..." -ForegroundColor Yellow

$vbCablePath = Join-Path $scriptDir "vb-cable"
$vbCableSetup = Join-Path $vbCablePath "VBCABLE_Setup_x64.exe"

if (-not (Test-Path $vbCableSetup)) {
    Write-Host "Downloading VB-Cable..." -ForegroundColor Yellow

    if (-not (Test-Path $vbCablePath)) {
        New-Item -ItemType Directory -Path $vbCablePath -Force | Out-Null
    }

    $zipPath = Join-Path $vbCablePath "VBCABLE_Driver_Pack.zip"
    Invoke-WebRequest -Uri "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip" -OutFile $zipPath

    Expand-Archive -Path $zipPath -DestinationPath $vbCablePath -Force
    Write-Host "VB-Cable downloaded and extracted" -ForegroundColor Green
}
else {
    Write-Host "VB-Cable files found" -ForegroundColor Green
}

# Step 3: Create output directory
Write-Host ""
Write-Host "Step 3: Preparing output directory..." -ForegroundColor Yellow

$outputDir = Join-Path $scriptDir "output"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Step 4: Compile installer
Write-Host ""
Write-Host "Step 4: Compiling installer..." -ForegroundColor Yellow

$issFile = Join-Path $scriptDir "TgdSoundboard.iss"

& $InnoSetupPath $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Installer compilation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== BUILD COMPLETE ===" -ForegroundColor Green
Write-Host ""
Write-Host "Installer created at:" -ForegroundColor Cyan
Get-ChildItem -Path $outputDir -Filter "*.exe" | ForEach-Object {
    Write-Host "  $($_.FullName)" -ForegroundColor White
}
Write-Host ""
