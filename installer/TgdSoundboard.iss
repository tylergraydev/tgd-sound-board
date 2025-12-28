; TGD Soundboard Installer Script
; Requires Inno Setup 6.0 or later

#define MyAppName "TGD Soundboard"
#define MyAppVersion "2.0.1"
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

[Files]
; Main application files
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
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
    MsgBox('To capture audio in Streamlabs/OBS:' + #13#10 + #13#10 +
           '1. Open Streamlabs Desktop' + #13#10 +
           '2. Add Source > Application Audio Capture' + #13#10 +
           '3. Select "TGD Soundboard"' + #13#10 + #13#10 +
           'No virtual audio cables needed!',
           mbInformation, MB_OK);
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TgdSoundboard"
