; Inno Setup 6 script for OpenIsland.
; Build: ISCC.exe OpenIsland.iss /DMyAppVersion=1.2.7
; By default it packages the portable build from ..\bin\installer-input\OpenIsland.exe.
; The app is per-user (localappdata), so the built-in updater can replace the exe
; without elevation, and no admin rights are required to install.

#ifndef MyAppVersion
  #define MyAppVersion "1.2.7"
#endif

#ifndef SourceExe
  #define SourceExe "..\bin\installer-input\OpenIsland.exe"
#endif

#define MyAppName "OpenIsland"
#define MyAppPublisher "OpenIsland"
#define MyAppURL "https://github.com/mmmatveyz/OpenIsland"
#define MyAppExeName "OpenIsland.exe"

[Setup]
AppId={{6F3B8A2E-9C4D-4E7B-A1F2-0D5E7C9B4A31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\bin\installer-out
OutputBaseFilename=OpenIslandSetup-{#MyAppVersion}
SetupIconFile=..\desktop\OpenIsland\Assets\OpenIsland.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.AutostartTask=Запускать {#MyAppName} при входе в Windows
english.AutostartTask=Start {#MyAppName} when signing in to Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OpenIsland"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
