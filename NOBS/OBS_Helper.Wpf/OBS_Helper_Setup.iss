; OBS帮助助手（WPF 版）Windows 安装包脚本（Inno Setup 6）
;
; 源目录：OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish
;    （自包含发布，含 .NET 运行时；界面与知识库都在程序集内，无需附带站点文件）
; 输出目录：NOBS\PAKE\windows
;
; 脚本内所有路径相对本 .iss 所在目录（OBS_Helper.Wpf），可在任意机器上构建。
; 正常由 ..\build.ps1 调用，也可以直接用 ISCC.exe 单独编译。
;
; 【编码】本文件按 UTF-8 **带 BOM** 保存：Inno Setup 6 见到 BOM 一定按 UTF-8 解析（实测无 BOM 时
; 它也会自动识别 UTF-8，带上 BOM 只是更明确 —— 换编辑器或换旧版 ISCC 都不会退化成按 ANSI 读，
; 那会让中文整片变成乱码）。改动本文件后请确认 BOM 没有被编辑器丢掉。

#define MyAppName "OBS帮助助手"
; 版本号默认与 csproj 对齐；build.ps1 会用 /DMyAppVersion=<ver> 覆盖此值。
; 用 #ifndef：ISPP 中命令行 /D 定义过的符号在脚本里不应再 #define 覆盖。
#ifndef MyAppVersion
#define MyAppVersion "2.9.6"
#endif
; 发布产物所在的 TFM 子目录（V2.9.3 起双目标：主构建 / Win7 兼容构建）
#ifndef MyAppTfm
#define MyAppTfm "net10.0-windows"
#endif
; 安装包文件名后缀（主构建留空，兼容构建给 "_win7"）
#ifndef MyAppOutputSuffix
#define MyAppOutputSuffix ""
#endif
; 最低 Windows 版本（Inno 的 MinVersion）：
;   · 主构建 net10.0-windows → 10.0（.NET 10 最低只支持 Windows 10）；
;   · 兼容构建 net6.0-windows → 6.1sp1（.NET 6 是最后一个支持 Win7 SP1 的版本）。
#ifndef MyAppMinVersion
#define MyAppMinVersion "10.0"
#endif
#define MyAppPublisher "OBS Helper"
#define MyAppExeName "OBS_Helper.exe"
; AppId 与旧的 Blazor 版不同：两版可以并存安装，升级路径互不干扰。
; AppId 固定不变的另一个理由：安装目录 / 卸载项 / 升级识别都不随安装语言变化。
;
; 【两个 TFM 共用同一个 AppId 是有意为之】同一款应用的「主构建」与「Win7 兼容构建」
; 在用户眼里就是一个软件：共用 AppId，Inno 才会把两者之间的切换识别成升级而不是并存装两份。
#define MyAppId "{{4C9F2D18-5B63-4A7E-8E21-9D3A6C4B1F72}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 目录名与 AppName 一样固定为中文品牌名：Inno 会记住这个目录并在升级时复用，
; 若随语言变化，英文用户重装会装到第二个目录、应用内的增量更新也会找不到目标文件。
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; 卸载项的显示名跟随安装语言（{cm:} 在这条指令上是受支持的）
UninstallDisplayName={cm:UninstallDisplayName}
UninstallDisplayIcon={app}\{#MyAppExeName}

OutputDir=..\PAKE\windows
OutputBaseFilename=OBS_Helper_Setup_{#MyAppVersion}{#MyAppOutputSuffix}
SetupIconFile=Assets\appicon.ico
LicenseFile=..\LICENSE

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoCopyright=Copyright (c) 2026 OBS Helper

Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 语言选择（V2.9.2）：先在向导里让用户选语言，默认中文。
; LanguageDetectionMethod=none → 不做系统语言探测，直接用 [Languages] 的第一条（简体中文）；
; ShowLanguageDialog=yes → 明确要求弹出语言选择页，不受语言条数影响。
ShowLanguageDialog=yes
LanguageDetectionMethod=none
; 自包含发布只有 x64 产物，装到 32 位系统上跑不起来，直接拦掉
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; 最低系统版本（V2.9.3）：由 build.ps1 按 TFM 传入。
;   主构建 → 10.0；Win7 兼容构建 → 6.1sp1。
; 这一条是「装得上但跑不起来」的最后一道拦截：错误的 TFM 装到错误系统上，
; 用户看到的是「双击没反应」，而不是一句能照做的提示。
MinVersion={#MyAppMinVersion}

[Languages]
; 顺序即默认值：第一条是简体中文（默认语言），第二条是英文。
; 两条都指向 .isl 消息文件，安装向导自身的文字也随之切换。
; 选定结果经 [INI] 写入 {app}\language.ini，应用首启时读取（见 Services/LocalizationService）。
;
; 【中文语言文件随仓库固定】Inno Setup 官方发行包**不含**简体中文（属社区翻译），
; 各渠道装出来的 Inno 是否带这个文件并不一致 —— CI 用 choco 装的 Inno 就没有，
; 本机 6.5 的安装却自带，于是同一份脚本在两边表现不同（CI 报
; Couldn't open include file "…\Languages\ChineseSimplified.isl"）。
; 这里把 installer\ChineseSimplified.isl 一并入库并显式引用，让本地与 CI 编译输入完全一致、
; 翻译版本也被钉住（该文件为 MIT 许可，文件头保留了原作者归属与来源）。
Name: "chinesesimplified"; MessagesFile: "installer\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
; 随安装语言切换的可见文案（快捷方式 / 任务 / 完成页）
chinesesimplified.UninstallDisplayName=OBS帮助助手
english.UninstallDisplayName=OBS Helper
chinesesimplified.AppShortcut=OBS帮助助手
english.AppShortcut=OBS Helper
chinesesimplified.UninstallShortcut=卸载 OBS帮助助手
english.UninstallShortcut=Uninstall OBS Helper
chinesesimplified.DesktopIcon=创建桌面快捷方式(&D)
english.DesktopIcon=Create a &desktop shortcut
chinesesimplified.AdditionalIcons=附加任务：
english.AdditionalIcons=Additional tasks:
chinesesimplified.LaunchApp=安装完成后启动 OBS帮助助手
english.LaunchApp=Launch OBS Helper when the installation finishes

[Files]
Source: "bin\Release\{#MyAppTfm}\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[INI]
; 安装时选定的语言 → 应用首启的默认语言。
; 应用侧只在该文件存在、且用户**没有**在应用内选过语言时采用它，因此升级 / 重装不会覆盖用户的选择。
Filename: "{app}\language.ini"; Section: "app"; Key: "language"; String: "{language}"

[Icons]
Name: "{group}\{cm:AppShortcut}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{cm:AppShortcut}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 用户的偏好与加密的凭据存在 %LocalAppData%\OBS_Helper 下。
; 这里只在卸载时清掉应用自己写的文件，不删整个目录，避免误伤。
Type: files; Name: "{app}\language.ini"
Type: files; Name: "{localappdata}\OBS_Helper\prefs.json"
Type: files; Name: "{localappdata}\OBS_Helper\secrets.dat"
Type: dirifempty; Name: "{localappdata}\OBS_Helper"
