<#
.SYNOPSIS
    Build and Package Installer for GDI SRT Embedded Native Desktop & Node Service.

.DESCRIPTION
    1. Validates all required build tools (.NET 8 SDK, Inno Setup ISCC, FFmpeg binaries).
    2. Runs test suite across all modules (GDI.Core, GDI.SRT.Engine, GDI.Encoder, GDI.Decoder, GDI.Multiview, etc.).
    3. Publishes self-contained win-x64 binaries for GDI.Desktop and GDI.Node.
    4. Packages FFmpeg/FFplay/FFprobe tools and WebRemote static assets.
    5. Compiles Windows Installer Setup (.exe) via Inno Setup with LZMA2 solid compression.
    6. Verifies output installer file and computes SHA256 checksum.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = Split-Path -Parent $scriptDir

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host " GDI SRT EMBEDDED — AUTOMATED MODULE VERIFICATION & INSTALLER BUILD" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "Workspace Root : $workspaceRoot"
Write-Host "Build Target   : win-x64 (Self-Contained .NET 8 LTS)"
Write-Host "Configuration  : $Configuration"
Write-Host "App Version    : $Version"
Write-Host ""

# -----------------------------------------------------------------------------
# STEP 1: Verify Build Prerequisites
# -----------------------------------------------------------------------------
Write-Host "[1/6] Verifying Build Tools and Environment..." -ForegroundColor Yellow

$dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    throw "dotnet SDK command not found. Please install .NET 8 SDK."
}
$dotnetVer = & dotnet --version
Write-Host "  -> .NET SDK Version: $dotnetVer" -ForegroundColor Green

# Find ISCC.exe
$isccCandidates = @(
    "c:\Users\GEDE NEW\AppData\Local\Programs\Antigravity IDE\resources\app\node_modules\innosetup\bin\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
)

$isccPath = $null
foreach ($cand in $isccCandidates) {
    if (Test-Path $cand) {
        $isccPath = $cand
        break
    }
}

if (-not $isccPath) {
    $whereIscc = where.exe iscc 2>$null
    if ($whereIscc -and (Test-Path $whereIscc[0])) {
        $isccPath = $whereIscc[0]
    }
}

if (-not $isccPath) {
    throw "Inno Setup Compiler (ISCC.exe) not found. Please install Inno Setup 6."
}
Write-Host "  -> Inno Setup Compiler : $isccPath" -ForegroundColor Green

# Verify FFmpeg tools
$ffmpegBin = Join-Path $workspaceRoot "tools\ffmpeg\bin"
$requiredTools = @("ffmpeg.exe", "ffplay.exe", "ffprobe.exe")
foreach ($tool in $requiredTools) {
    $tp = Join-Path $ffmpegBin $tool
    if (-not (Test-Path $tp)) {
        throw "Required broadcast tool missing: $tp"
    }
}
Write-Host "  -> FFmpeg binaries verified in $ffmpegBin" -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 2: Run Automated Tests Across All Modules
# -----------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host "`n[2/6] Running Unit & Integration Tests across all modules..." -ForegroundColor Yellow
    $slnPath = Join-Path $workspaceRoot "GDI-SRT-Embedded.sln"
    & dotnet test $slnPath -c $Configuration --nologo --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed with exit code $LASTEXITCODE. Build aborted."
    }
    Write-Host "  -> All test suites passed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n[2/6] Skipping tests as requested (-SkipTests)." -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------------
# STEP 3: Create Local Staging Directory (Avoiding Network Drive SMB Locks)
# -----------------------------------------------------------------------------
Write-Host "`n[3/6] Setting up Local Staging Area..." -ForegroundColor Yellow
$tempRoot = Join-Path $env:TEMP ("gdi_installer_build_" + [System.Guid]::NewGuid().ToString("N"))
$stagingPayload = Join-Path $tempRoot "payload"
$stagingOutput = Join-Path $tempRoot "output"

New-Item -ItemType Directory -Path $stagingPayload -Force | Out-Null
New-Item -ItemType Directory -Path $stagingOutput -Force | Out-Null
Write-Host "  -> Staging path: $stagingPayload" -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 4: Publish Self-Contained Desktop & Node Modules
# -----------------------------------------------------------------------------
Write-Host "`n[4/6] Publishing Self-Contained Assemblies (.NET 8 win-x64)..." -ForegroundColor Yellow

$desktopProj = Join-Path $workspaceRoot "src\GDI.Desktop\GDI.Desktop.csproj"
Write-Host "  -> Publishing GDI.Desktop..."
& dotnet publish $desktopProj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $stagingPayload --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish GDI.Desktop."
}

$nodeProj = Join-Path $workspaceRoot "src\GDI.Node\GDI.Node.csproj"
Write-Host "  -> Publishing GDI.Node..."
& dotnet publish $nodeProj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $stagingPayload --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish GDI.Node."
}

# Copy FFmpeg toolset
Write-Host "  -> Packaging broadcast FFmpeg toolset..."
$destFfmpegBin = Join-Path $stagingPayload "tools\ffmpeg\bin"
New-Item -ItemType Directory -Path $destFfmpegBin -Force | Out-Null
Copy-Item -Path "$ffmpegBin\*" -Destination $destFfmpegBin -Force

# Verify Web Remote static assets
$wwwrootSrc = Join-Path $workspaceRoot "src\GDI.Node\wwwroot"
$destWwwroot = Join-Path $stagingPayload "wwwroot"
if (Test-Path $wwwrootSrc) {
    Write-Host "  -> Packaging Web Remote static assets..."
    Copy-Item -Path $wwwrootSrc -Destination $stagingPayload -Recurse -Force
}

# Verify critical payload files
$crucialFiles = @(
    (Join-Path $stagingPayload "GDI.Desktop.exe"),
    (Join-Path $stagingPayload "GDI.Node.exe"),
    (Join-Path $destFfmpegBin "ffmpeg.exe"),
    (Join-Path $destFfmpegBin "ffplay.exe"),
    (Join-Path $destFfmpegBin "ffprobe.exe")
)
foreach ($cf in $crucialFiles) {
    if (-not (Test-Path $cf)) {
        throw "Crucial file was not produced in payload: $cf"
    }
}
Write-Host "  -> Self-contained payload verified." -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 5: Compile Inno Setup Windows Installer
# -----------------------------------------------------------------------------
Write-Host "`n[5/6] Compiling Native Windows Installer with Inno Setup..." -ForegroundColor Yellow

$issScript = Join-Path $workspaceRoot "deploy\windows\installer\GDI-SRT-Embedded-Native.iss"
if (-not (Test-Path $issScript)) {
    throw "Inno Setup script not found at: $issScript"
}

$isccArgs = @(
    "/Q",
    "/DSourceDir=$stagingPayload",
    "/DAppVersion=$Version",
    "/O$stagingOutput",
    "/FGDI-SRT-Embedded-Native-Windows-x64-Setup",
    $issScript
)

Write-Host "  -> Running ISCC compilation (LZMA2 Solid Compression)..."
& $isccPath $isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
Write-Host "  -> Inno Setup compiled successfully!" -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 6: Deploy & Verify Output Artifacts
# -----------------------------------------------------------------------------
Write-Host "`n[6/6] Finalizing Artifacts & Checksums..." -ForegroundColor Yellow

$compiledExe = Join-Path $stagingOutput "GDI-SRT-Embedded-Native-Windows-x64-Setup.exe"
if (-not (Test-Path $compiledExe)) {
    throw "Expected setup exe not found: $compiledExe"
}

$artifactsDir = Join-Path $workspaceRoot "artifacts"
if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

$destArtifact = Join-Path $artifactsDir "GDI-SRT-Embedded-Native-Windows-x64-Setup.exe"
$destRoot = Join-Path $workspaceRoot "GDI-SRT-Embedded-Native-Windows-x64-Setup.exe"

# Copy to artifacts directory and root
Copy-Item -Path $compiledExe -Destination $destArtifact -Force
Copy-Item -Path $compiledExe -Destination $destRoot -Force

# Clean up staging directory
try {
    Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
} catch { }

# Compute SHA256 and details
$fileItem = Get-Item $destArtifact
$hash = (Get-FileHash -Path $destArtifact -Algorithm SHA256).Hash
$sizeMb = [math]::Round($fileItem.Length / 1MB, 2)
$sw.Stop()

Write-Host "`n====================================================================" -ForegroundColor Green
Write-Host " BUILD & PACKAGING COMPLETED SUCCESSFULLY in $($sw.Elapsed.ToString('mm\:ss'))" -ForegroundColor Green
Write-Host "====================================================================" -ForegroundColor Green
Write-Host " Installer File : $destArtifact" -ForegroundColor Cyan
Write-Host " Root Shortcut  : $destRoot" -ForegroundColor Cyan
Write-Host " File Size      : $sizeMb MB ($($fileItem.Length.ToString('N0')) bytes)" -ForegroundColor Cyan
Write-Host " SHA256 Hash    : $hash" -ForegroundColor Cyan
Write-Host " Target Arch    : Windows 10/11 x64 (Self-Contained)" -ForegroundColor Cyan
Write-Host " Modules Built  :" -ForegroundColor Cyan
Write-Host "   - GDI.Core (Models, Vault, Realtime Distributor, True Audio DSP Meter)"
Write-Host "   - GDI.Hardware (DeckLink Scheduled Playout, AJA, Magewell, DirectShow)"
Write-Host "   - GDI.SRT.Engine (RFC-compliant SRT URLs, Socket Tuning, astats Parser)"
Write-Host "   - GDI.Encoder (NVENC/QSV/AMF/CPU, bwdif deinterlace, bt709 matrix)"
Write-Host "   - GDI.Decoder (SRT caller/listener receiver, astats audio analysis)"
Write-Host "   - GDI.Multiview (1/2/4/6/8/9/16 layout, unsafe WriteableBitmap blit, VU meters)"
Write-Host "   - GDI.ProgramReturn (Studio Send & Venue Receive pipeline)"
Write-Host "   - GDI.Network (NIC detection, real-time Mbps RX/TX calculation)"
Write-Host "   - GDI.Node (REST API daemon, Web Remote on port 8787)"
Write-Host "   - GDI.Desktop (Native WPF Obsidian Dark Broadcast Application)"
Write-Host "   - Broadcast Tools (FFmpeg, FFplay, FFprobe 7.1)"
Write-Host "====================================================================`n" -ForegroundColor Green
