; Builds KwartoTV.exe (the installer). Run from the repo root:
;   iscc /DAppVersion=1.0.0 installer\KwartoTV.iss
; Expects the published app in .\publish (dotnet publish ... -o publish) including libmpv-2.dll.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B0D9A2C4-5E61-4B0B-9D3C-6C1F1E5D7A10}
AppName=KwartoTV
AppVersion={#AppVersion}
AppPublisher=KwartoTV
DefaultDirName={autopf}\KwartoTV
DefaultGroupName=KwartoTV
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=KwartoTV
SetupIconFile=..\src\KwartoTV.App\Assets\kwartotv.ico
UninstallDisplayIcon={app}\KwartoTV.exe
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
AppMutex=KwartoTV.SingleInstance
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: "autostart"; Description: "Start KwartoTV when Windows starts (recommended for a living-room PC)"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\KwartoTV"; Filename: "{app}\KwartoTV.exe"
Name: "{autodesktop}\KwartoTV"; Filename: "{app}\KwartoTV.exe"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KwartoTV"; ValueData: """{app}\KwartoTV.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Open the phone remote ports on any network profile
; (Windows often marks home Wi-Fi as Public). Pairing still needs the code shown on the TV screen.
; (Older versions used an http.sys URL reservation; remove it.)
Filename: "{sys}\netsh.exe"; Parameters: "http delete urlacl url=http://+:8765/"; Flags: runhidden; StatusMsg: "Preparing the remote…"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV app"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""KwartoTV app"" dir=in action=allow program=""{app}\KwartoTV.exe"" profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV remote"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""KwartoTV remote"" dir=in action=allow protocol=TCP localport=8765 profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV discovery"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""KwartoTV discovery"" dir=in action=allow protocol=UDP localport=5353 profile=any"; Flags: runhidden
Filename: "{app}\KwartoTV.exe"; Description: "Start KwartoTV"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "http delete urlacl url=http://+:8765/"; Flags: runhidden; RunOnceId: "DelUrlAcl"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV app"""; Flags: runhidden; RunOnceId: "DelFw0"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV remote"""; Flags: runhidden; RunOnceId: "DelFw1"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwartoTV discovery"""; Flags: runhidden; RunOnceId: "DelFw2"
