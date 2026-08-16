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
// Helper to detect if .NET 9 Desktop Runtime (x64) is installed
function IsDotNet9DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  SharedPath: String;
begin
  Result := False;
  
  // 1. Check standard 64-bit Program Files shared runtime path
  SharedPath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(SharedPath) then
  begin
    if FindFirst(SharedPath + '\9.*', FindRec) then
    begin
      try
        Result := True;
      finally
        FindClose(FindRec);
      end;
      Exit;
    end;
  end;

  // 2. Fallback check standard Program Files
  SharedPath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(SharedPath) then
  begin
    if FindFirst(SharedPath + '\9.*', FindRec) then
    begin
      try
        Result := True;
      finally
        FindClose(FindRec);
      end;
      Exit;
    end;
  end;
end;

// Helper to close existing running instances of Relay before installation/upgrade
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  ShellExec('open', 'taskkill.exe', '/IM Relay.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);

#if Defined(FrameworkDependent)
  if not IsDotNet9DesktopRuntimeInstalled() then
  begin
    if MsgBox('Relay requires the Microsoft .NET 9 Desktop Runtime (x64) to operate smoothly.' + #13#10 + #13#10 +
              'Would you like to open Microsoft''s official download page to install it now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0/runtime', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
#endif
end;

function InitializeUninstall(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  ShellExec('open', 'taskkill.exe', '/IM Relay.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;
