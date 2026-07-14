#define MyAppName "OpenGameMate"
#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif
#define MyAppExeName "OpenGameMate.App.exe"
#ifndef MySourceDirectory
#define MySourceDirectory "..\artifacts\release\OpenGameMate-v0.1.0-win-x64"
#endif
#ifndef MyInstallerOutputDirectory
#define MyInstallerOutputDirectory "..\artifacts\installer"
#endif

[Setup]
AppId={{7E204AA3-8519-49A1-929D-5AC91312E176}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\OpenGameMate
DefaultGroupName=OpenGameMate
OutputDir={#MyInstallerOutputDirectory}
OutputBaseFilename=OpenGameMate-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
LicenseFile=..\LICENSE
SetupIconFile=..\assets\OpenGameMate.AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MySourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OpenGameMate"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\OpenGameMate"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
