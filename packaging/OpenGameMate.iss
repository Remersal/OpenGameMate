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

[CustomMessages]
english.GameAccountRiskTitle=Game account risk warning
english.GameAccountRiskDescription=Read this notice before continuing.
english.GameAccountRiskDetails=Using third-party companion or automation software may be restricted by a game's or platform's terms and may result in warnings, restrictions, suspension, or a permanent account ban. OpenGameMate does not inject into games or read game memory, but it cannot guarantee that a game, anti-cheat system, or platform will consider its use permitted. Check the applicable rules and continue only if you accept this risk.
english.GameAccountRiskAccept=I understand the possible account-ban risk and choose to continue.
english.GameAccountRiskRequired=You must explicitly accept the game account risk notice before installation. Silent first-time installation is not allowed.
chinesesimplified.GameAccountRiskTitle=游戏账号风险提示
chinesesimplified.GameAccountRiskDescription=继续安装前，请认真阅读并确认以下风险。
chinesesimplified.GameAccountRiskDetails=使用第三方陪玩或自动化工具可能受到游戏或平台服务条款限制，并可能导致警告、功能限制、暂时封禁或永久封禁账号。OpenGameMate 不注入游戏，也不读取游戏内存，但无法保证任何游戏、反作弊系统或平台会允许其使用。请先确认适用规则，仅在你愿意承担风险时继续。
chinesesimplified.GameAccountRiskAccept=我已了解可能存在的封号风险，并选择继续安装。
chinesesimplified.GameAccountRiskRequired=首次安装必须明确确认游戏账号风险；静默安装不能跳过此确认。

[Code]
var
  GameAccountRiskPage: TInputOptionWizardPage;
  GameAccountRiskPreviouslyAccepted: Boolean;

procedure InitializeWizard;
begin
  GameAccountRiskPreviouslyAccepted :=
    GetPreviousData('GameAccountRiskAccepted', '') = '1';

  GameAccountRiskPage := CreateInputOptionPage(
    wpWelcome,
    ExpandConstant('{cm:GameAccountRiskTitle}'),
    ExpandConstant('{cm:GameAccountRiskDescription}'),
    ExpandConstant('{cm:GameAccountRiskDetails}'),
    False,
    True);
  GameAccountRiskPage.Add(ExpandConstant('{cm:GameAccountRiskAccept}'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result :=
    (PageID = GameAccountRiskPage.ID) and
    GameAccountRiskPreviouslyAccepted;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if (not GameAccountRiskPreviouslyAccepted) and
     (not GameAccountRiskPage.Values[0]) then
    Result := ExpandConstant('{cm:GameAccountRiskRequired}');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if GameAccountRiskPreviouslyAccepted or GameAccountRiskPage.Values[0] then
    SetPreviousData(PreviousDataKey, 'GameAccountRiskAccepted', '1');
end;
