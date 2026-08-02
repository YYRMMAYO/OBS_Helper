; OBS 排障助手 Windows 安装包脚本（Inno Setup 6）
; 源目录：OBS_Helper.Win\dist（自包含发布，含 .NET 运行时与站点内容）

#define MyAppName "OBS 排障助手"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "OBS Helper"
#define MyAppExeName "OBS_Helper.exe"
#define MyAppId "{{8E3A1B2C-7D44-4F2A-9C10-2B5E6F7A8D01}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=F:\OBS\OBS_Helper.Win\installer
OutputBaseFilename=OBS_Helper_Setup_{#MyAppVersion}
SetupIconFile=F:\OBS\OBS_Helper.Win\appicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
; 安装包以管理员权限写入 Program Files
PrivilegesRequired=admin

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "F:\OBS\OBS_Helper.Win\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
