; OBS 排障助手 Windows 安装包脚本（Inno Setup 6）
; 源目录：OBS_Helper.Win\bin\Release\net10.0-windows10.0.19041.0\publish
;    （自包含发布，含 .NET 运行时 + 站点 wwwroot，由 build.ps1 生成）
; 输出目录：仓库根 PAKE\windows（不论系统，安装包与软件统一落到 PAKE）
;
; 说明：脚本内路径相对本 .iss 所在目录（OBS_Helper.Win），因此可在任意机器上构建。

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
; 统一输出到 PAKE\windows（相对本脚本目录的上级 PAKE\windows）
OutputDir=..\PAKE\windows
OutputBaseFilename=OBS_Helper_Setup_{#MyAppVersion}
SetupIconFile=appicon.ico
; 安装向导中展示 MIT 许可证，供最终用户确认
LicenseFile=..\..\LICENSE
; 安装包/文件元数据（提升“属性-详细信息”完整度）
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoCopyright=Copyright (c) 2026 OBS Helper
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
; 安装包以管理员权限写入 Program Files
PrivilegesRequired=admin

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Files]
; 打包自包含发布目录（运行时 + 站点 wwwroot 一并安装）
Source: "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 随安装包附带 MIT 许可证文本，便于最终用户查看
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
