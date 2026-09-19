#define MyAppName "续航助手"
#define MyAppVersion "3.1.0"
#define MyAppExeName "BatteryPilot.exe"

[Setup]
AppId={{B642E097-6D12-4B23-AB01-DC0A1C2553AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=BatteryPilot contributors
DefaultDirName={localappdata}\Programs\BatteryPilot
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\dist
OutputBaseFilename=BatteryPilot-Setup-{#MyAppVersion}-x64
SetupIconFile=..\BatteryPilot.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
LicenseFile=..\LICENSE
InfoBeforeFile=..\适配说明.md
VersionInfoVersion=3.1.0.0
VersionInfoProductName=BatteryPilot
VersionInfoDescription=续航助手安装程序
SetupLogging=yes

[Languages]
Name: "chinesesimp"; MessagesFile: ".\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "..\BatteryPilot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\BatteryPilot.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\BatteryPilot.ico"; DestDir: "{app}"; DestName: "BatteryPilot-minimal.ico"; Flags: ignoreversion
Source: "..\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\适配说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--restore-and-exit"; StatusMsg: "正在检查并恢复遗留电源方案…"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "启动{#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppPath: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  AppPath := ExpandConstant('{app}\{#MyAppExeName}');
  if not FileExists(AppPath) then
    Exit;

  if not Exec(AppPath, '--restore-and-exit', ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('无法启动电源方案恢复程序。卸载已中止，应用文件会保留。', mbError, MB_OK);
    Abort;
  end;

  if ResultCode <> 0 then
  begin
    MsgBox('未能恢复原电源方案，卸载已中止。请先退出续航助手后重试；错误记录位于 %LOCALAPPDATA%\BatteryPilot\restore-error.txt。', mbError, MB_OK);
    Abort;
  end;
end;
