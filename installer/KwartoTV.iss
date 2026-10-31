; Builds KwartoTV.exe (the installer). Run from the repo root:
;   iscc /DAppVersion=1.0.0 installer\KwartoTV.iss
; Expects the published app in .\publish (dotnet publish ... -o publish) including libmpv-2.dll.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Installed for the current user (in %LocalAppData%\Programs\KwartoTV), so updates replace the files with no Windows
; permission prompt. The firewall rules for the phone remote are the only admin step, done once (setup-admin.ps1).
; A new AppId: the old Program Files install ({B0D9A2C4-...}) is moved out by setup-admin.ps1.
AppId={{6E0C5E1B-2F4A-4C8E-9A57-3B1D0F7C2E44}
AppName=KwartoTV
AppVersion={#AppVersion}
AppPublisher=KwartoTV
DefaultDirName={autopf}\KwartoTV
DefaultGroupName=KwartoTV
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=KwartoTV
SetupIconFile=..\src\KwartoTV.App\Assets\kwartotv.ico
UninstallDisplayIcon={app}\KwartoTV.exe
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: "autostart"; Description: "Start KwartoTV when Windows starts (recommended for a living-room PC)"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "setup-admin.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\KwartoTV"; Filename: "{app}\KwartoTV.exe"
Name: "{autodesktop}\KwartoTV"; Filename: "{app}\KwartoTV.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KwartoTV"; ValueData: """{app}\KwartoTV.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\KwartoTV"; Flags: uninsdeletekeyifempty

[Run]
Filename: "{app}\KwartoTV.exe"; Description: "Start KwartoTV"; Flags: nowait postinstall skipifsilent

[Code]
const
  AppMutexName = 'KwartoTV.SingleInstance';
  OldUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B0D9A2C4-5E61-4B0B-9D3C-6C1F1E5D7A10}_is1';

var
  Migrated: Boolean;

// An in-app update starts this installer just before KwartoTV closes. Wait for it to close
// instead of quitting (a silent install used to stop right here, so nothing was updated).
function InitializeSetup: Boolean;
var
  I, Code: Integer;
begin
  Result := True;
  I := 0;
  while CheckForMutexes(AppMutexName) and (I < 60) do
  begin
    Sleep(500);
    I := I + 1;
  end;
  if CheckForMutexes(AppMutexName) then
  begin
    if WizardSilent then
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KwartoTV.exe', '', SW_HIDE, ewWaitUntilTerminated, Code)
    else
      while CheckForMutexes(AppMutexName) do
        if MsgBox('KwartoTV is open. Close it, then click OK to continue.', mbError, MB_OKCANCEL) = IDCANCEL then
        begin
          Result := False;
          Exit;
        end;
    Sleep(1000);
  end;
end;

// The old KwartoTV in Program Files (needed a permission prompt for every update). Returns its uninstaller, or ''.
function OldUninstaller: String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, OldUninstallKey, 'UninstallString', S) or RegQueryStringValue(HKLM32, OldUninstallKey, 'UninstallString', S) then
    Result := RemoveQuotes(S);
end;

function PowerShell: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// The one admin step (firewall rules, moving the old install out). Asked for on a normal install, or when an old
// Program Files install has to be moved; a silent update that already has its rules never asks.
procedure AdminSetup;
var
  Old, Done: String;
  Code: Integer;
begin
  Old := OldUninstaller;
  if not RegQueryStringValue(HKCU, 'Software\KwartoTV', 'SetupDoneFor', Done) then Done := '';
  if (Old = '') and (CompareText(Done, ExpandConstant('{app}')) = 0) then Exit;
  if (Old = '') and WizardSilent and (Done <> '') then Exit;
  WizardForm.StatusLabel.Caption := 'Letting your phone reach the TV (Windows asks permission once)…';
  if ShellExec('runas', PowerShell,
      '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + ExpandConstant('{app}\setup-admin.ps1') + '" -App "' + ExpandConstant('{app}') + '"' +
      ' -OldUninstaller "' + Old + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    RegWriteStringValue(HKCU, 'Software\KwartoTV', 'SetupDoneFor', ExpandConstant('{app}'));
    Migrated := Old <> '';
  end
  else
    Log('Admin setup was not allowed: the phone remote may need "Fix network access" in KwartoTV Settings.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then AdminSetup;
  // An in-app update (or the move out of Program Files) opens KwartoTV again. Through Explorer, so it runs as the
  // normal user even when this installer was started with admin rights.
  if (CurStep = ssDone) and WizardSilent and (Migrated or (ExpandConstant('{param:RELAUNCH|0}') = '1')) then
    Exec(ExpandConstant('{win}\explorer.exe'), '"' + ExpandConstant('{app}\KwartoTV.exe') + '"', '', SW_SHOW, ewNoWait, Code);
end;

function InitializeUninstall: Boolean;
var
  Code: Integer;
begin
  Result := True;
  if CheckForMutexes(AppMutexName) then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KwartoTV.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  // Remove the firewall rules (the second and last admin step).
  if CurUninstallStep = usUninstall then
  begin
    ShellExec('runas', PowerShell, '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + ExpandConstant('{app}\setup-admin.ps1') + '" -Remove',
      '', SW_HIDE, ewWaitUntilTerminated, Code);
    RegDeleteValue(HKCU, 'Software\KwartoTV', 'SetupDoneFor');
  end;
end;
