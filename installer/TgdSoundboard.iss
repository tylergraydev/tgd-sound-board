; TGD Soundboard Installer Script
; Requires Inno Setup 6.0 or later

#define MyAppName "TGD Soundboard"
#define MyAppVersion "1.4.0"
#define MyAppPublisher "TGD"
#define MyAppURL "https://github.com/tylergraydev/tgd-sound-board"
#define MyAppExeName "TgdSoundboard.exe"

[Setup]
; Basic installer settings
AppId={{A8E5C7F2-9B3D-4E6A-8F1C-2D5E7A9B3C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=output
OutputBaseFilename=TgdSoundboard_Setup_{#MyAppVersion}
SetupIconFile=
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "installvbcable"; Description: "Install VB-Cable Virtual Audio Device (required for audio routing)"; GroupDescription: "Virtual Audio Cable:"; Flags: checkedonce

[Files]
; Main application files
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; VB-Cable installer files
Source: "vb-cable\VBCABLE_Setup_x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion; Tasks: installvbcable
Source: "vb-cable\VBCABLE_Setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion; Tasks: installvbcable

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Run VB-Cable installer if selected
Filename: "{tmp}\VBCABLE_Setup_x64.exe"; Parameters: "-i -h"; StatusMsg: "Installing VB-Cable Virtual Audio Device..."; Flags: waituntilterminated runascurrentuser; Tasks: installvbcable; Check: IsWin64 and not IsVBCableInstalled
Filename: "{tmp}\VBCABLE_Setup.exe"; Parameters: "-i -h"; StatusMsg: "Installing VB-Cable Virtual Audio Device..."; Flags: waituntilterminated runascurrentuser; Tasks: installvbcable; Check: not IsWin64 and not IsVBCableInstalled

; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Check if VB-Cable is already installed by looking for the audio device
function IsVBCableInstalled: Boolean;
var
  RegKey: String;
begin
  Result := False;

  // Check for VB-Cable in the registry (audio devices)
  RegKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render';

  // Alternative: Check if the driver files exist
  if FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win7.sys')) then
  begin
    Result := True;
    Exit;
  end;

  if FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable_win7.sys')) then
  begin
    Result := True;
    Exit;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;

  // Check if running on Windows 10 or later (recommended)
  if not IsWin64 then
  begin
    if MsgBox('TGD Soundboard is optimized for 64-bit Windows. Continue anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Post-installation tasks if needed
    if WizardIsTaskSelected('installvbcable') and not IsVBCableInstalled then
    begin
      MsgBox('VB-Cable has been installed. You may need to restart your computer for the audio device to appear.',
             mbInformation, MB_OK);
    end;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TgdSoundboard"
