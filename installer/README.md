# TGD Soundboard Installer

This folder contains everything needed to build the Windows installer for TGD Soundboard.

## Prerequisites

1. **Inno Setup 6** - Download and install from: https://jrsoftware.org/isdl.php
2. **.NET 8 SDK** - Required to build the application

## Building the Installer

### Option 1: Automated Build (Recommended)

Simply run the build script:

```batch
build-installer.bat
```

Or using PowerShell:

```powershell
.\build-installer.ps1
```

This will:
1. Publish the application as a self-contained executable
2. Download VB-Cable if not already present
3. Compile the Inno Setup installer
4. Output the installer to `./output/`

### Option 2: Manual Build

1. Publish the application:
   ```batch
   cd ..
   dotnet publish src/TgdSoundboard/TgdSoundboard.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
   ```

2. Ensure VB-Cable files are in `./vb-cable/`:
   - Download from: https://vb-audio.com/Cable/
   - Extract to `./vb-cable/`

3. Open `TgdSoundboard.iss` in Inno Setup and compile

## Output

The compiled installer will be in:
```
./output/TgdSoundboard_Setup_1.0.0.exe
```

## What the Installer Does

1. Installs TGD Soundboard to `Program Files\TGD Soundboard`
2. Optionally installs VB-Cable virtual audio driver
3. Creates Start Menu shortcuts
4. Optionally creates Desktop shortcut
5. Registers for Windows uninstall

## VB-Cable

VB-Cable is bundled with permission as donationware. The installer checks if VB-Cable is already installed and only installs it if needed.

Users can also install VB-Cable manually from: https://vb-audio.com/Cable/

## Customization

Edit `TgdSoundboard.iss` to customize:
- App version (line 7)
- Publisher info (lines 8-9)
- Default installation directory
- Installer appearance
