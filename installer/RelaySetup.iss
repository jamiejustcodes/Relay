; =====================================================================
; Relay - Inno Setup Installer Script
; =====================================================================

#define MyAppName "Relay"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Relay"
#define MyAppExeName "Relay.exe"
#define MyAppAssocName "Relay Application"
#define MyAppIcon "..\src\Relay.UI\Assets\relay.ico"

[Setup]
AppId={{8F8B6C77-74B0-4A73-A38C-323C6BF01C08}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=RelaySetup
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={#MyAppIcon}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startwithwindows"; Description: "Start Relay automatically in background on PC boot (Recommended)"; GroupDescription: "Windows Startup Options:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,obfuscated,Mapping.txt"
Source: "{#MyAppIcon}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppIcon}"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\relay.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\relay.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Description: "Launch Relay now in background (System Tray)"; Flags: nowait postinstall skipifsilent

[Code]
// Helper to close existing running instances of Relay before installation/upgrade
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  ShellExec('open', 'taskkill.exe', '/IM Relay.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;

function InitializeUninstall(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  ShellExec('open', 'taskkill.exe', '/IM Relay.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;
