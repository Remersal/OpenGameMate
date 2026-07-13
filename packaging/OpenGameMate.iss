#define MyAppName "OpenGameMate"
#define MyAppVersion "0.1.0"
#define MyAppExeName "OpenGameMate.App.exe"

[Setup]
AppId={{7E204AA3-8519-49A1-929D-5AC91312E176}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\OpenGameMate
DefaultGroupName=OpenGameMate
OutputDir=..\artifacts\installer
OutputBaseFilename=OpenGameMate-Setup-0.1.0
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\release\OpenGameMate-v0.1.0-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OpenGameMate"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\OpenGameMate"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch OpenGameMate"; Flags: nowait postinstall skipifsilent
