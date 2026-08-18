; OBS 排障助手（WPF 版）Windows 安装包脚本（Inno Setup 6）
;
; 源目录：OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish
;    （自包含发布，含 .NET 运行时；界面与知识库都在程序集内，无需附带站点文件）
; 输出目录：NOBS\PAKE\windows
;
; 脚本内所有路径相对本 .iss 所在目录（OBS_Helper.Wpf），可在任意机器上构建。
; 正常由 ..\build.ps1 调用，也可以直接用 ISCC.exe 单独编译。

#define MyAppName "OBS 排障助手"
; 版本号默认与 csproj 对齐；build.ps1 会用 /DMyAppVersion=<ver> 覆盖此值。
; 用 #ifndef：ISPP 中命令行 /D 定义过的符号在脚本里不应再 #define 覆盖。
#ifndef MyAppVersion
#define MyAppVersion "1.10.0"
#endif
#define MyAppPublisher "OBS Helper"
#define MyAppExeName "OBS_Helper.exe"
; AppId 与旧的 Blazor 版不同：两版可以并存安装，升级路径互不干扰
#define MyAppId "{{4C9F2D18-5B63-4A7E-8E21-9D3A6C4B1F72}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

OutputDir=..\PAKE\windows
OutputBaseFilename=OBS_Helper_Setup_{#MyAppVersion}
SetupIconFile=Assets\appicon.ico
LicenseFile=..\LICENSE

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoCopyright=Copyright (c) 2026 OBS Helper

Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 自包含发布只有 x64 产物，装到 32 位系统上跑不起来，直接拦掉
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 用户的偏好与加密的凭据存在 %LocalAppData%\OBS_Helper 下。
; 这里只在卸载时清掉应用自己写的两个文件，不删整个目录，避免误伤。
Type: files; Name: "{localappdata}\OBS_Helper\prefs.json"
Type: files; Name: "{localappdata}\OBS_Helper\secrets.dat"
Type: dirifempty; Name: "{localappdata}\OBS_Helper"
