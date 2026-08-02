# OBS 排障助手 · Windows 桌面版

一个把「OBS 复杂问题排障知识库」打包成 **Windows 桌面程序（.exe）** 的壳应用。
基于 .NET 10 + WebView2，离线运行，内容与网页版完全一致。

## 两种使用方式

### 1）安装包（推荐分发）
- 文件：`installer\OBS_Helper_Setup_1.0.0.exe`
- 双击安装 → 自动创建「开始菜单」与「桌面快捷方式」→ 启动即用。
- 自包含（已内嵌 .NET 运行时），**目标机无需安装 .NET**。

### 2）便携版（免安装）
- 文件夹：`dist\`（或直接用 `OBS_Helper_Portable_1.0.0.zip` 解压）
- 直接双击 `dist\OBS_Helper.exe` 即可运行，可放在 U 盘/任意目录。

## 运行依赖
- 需要系统已安装 **Microsoft Edge WebView2 Runtime**（Windows 10/11 通常随 Edge 自带）。
  若启动时报「无法初始化内置浏览器」，请到微软官网下载安装 WebView2 Runtime 后重试。

## 功能
与网页版一致：首页 + 10 大分类、问题详情（可勾选分步方案）、搜索筛选、⭐收藏、
问答助手（离线关键词匹配）、配置诊断、直播间搭建向导。

## 目录结构
```
OBS_Helper.Win/
├─ OBS_Helper_Setup.iss      # Inno Setup 安装脚本
├─ appicon.ico               # 应用图标
├─ MainForm.cs / Program.cs  # 桌面壳源码（WebView2 承载站点）
├─ OBS_Helper.Win.csproj     # 工程文件
├─ dist/                     # 自包含发布产物（含 wwwroot 站点 + .NET 运行时）
└─ installer/                # 生成的安装包
```

## 重新构建
```bash
# 开发/调试
dotnet run --project OBS_Helper.Win

# 发布自包含 Windows x64
dotnet publish OBS_Helper.Win/OBS_Helper.Win.csproj -c Release -r win-x64 --self-contained true -o OBS_Helper.Win/dist

# 生成安装包（需本机已装 Inno Setup 6）
" C:\Program Files (x86)\Inno Setup 6\ISCC.exe" OBS_Helper.Win/OBS_Helper_Setup.iss
```

> 站点内容来自 `OBS_Helper.Client` 的 `dotnet publish` 输出（publish/wwwroot），
> 已整体复制到 `OBS_Helper.Win/wwwroot` 并随包发布。更新内容后重新发布即可。
