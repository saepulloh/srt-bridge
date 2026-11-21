#define AppName "GDI SRT Embedded Native Desktop"
#define AppVersion "1.0.0"
#define AppPublisher "Global Data Indonesia"
#define AppURL "https://www.gdi.co.id"
#define AppExeName "GDI.Desktop.exe"
#define NodeExeName "GDI.Node.exe"

#ifndef SourceDir
  #define SourceDir "..\..\..\artifacts\GDI-SRT-Embedded-Windows-x64"
#endif

[Setup]
AppId={{D370A11B-4E7B-4B3A-8F5E-37936E7B6C01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\GDI\SRT Embedded
DefaultGroupName=GDI SRT Embedded
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
OutputDir=..\..\..\artifacts
OutputBaseFilename=GDI-SRT-Embedded-Native-Windows-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartIfNeededByRun=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Start GDI SRT Embedded automatically when Windows starts"; GroupDescription: "System Startup:"

[Dirs]
Name: "{commonappdata}\GDI\SRT Embedded"; Permissions: system-full admins-full
Name: "{commonappdata}\GDI\SRT Embedded\config"; Permissions: system-full admins-full
Name: "{commonappdata}\GDI\SRT Embedded\security"; Permissions: system-full admins-full
Name: "{commonappdata}\GDI\SRT Embedded\logs"; Permissions: system-full admins-full

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Install-GDI.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Uninstall-GDI.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\GDI SRT Node Service"; Filename: "{app}\{#NodeExeName}"
Name: "{group}\GDI SRT Web Remote"; Filename: "http://localhost:8787"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GDISRTEmbedded"; ValueData: """{app}\{#AppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-GDI.ps1"" -InstallDir ""{app}"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring broadcast security, firewall, and data directories..."
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-GDI.ps1"""; Flags: runhidden waituntilterminated
