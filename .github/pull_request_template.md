# 拉取请求说明

> 请把下面各项填清楚。**「怎么验证的」比「改了什么」更重要** —— 审阅者首先要知道
> 这个改动凭什么算做完了。

## 改了什么

<!-- 一两句话说清范围；涉及多个模块就分条列。 -->

## 为什么这么改

<!-- 先说问题（用户会遇到什么、现状哪里不对），再说为什么选这个方案。
     如果考虑过别的做法并且放弃了，也写一句为什么 —— 这部分最难从 diff 里看出来。 -->

## 怎么验证的

- [ ] `dotnet test NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj -c Release` 全绿
- [ ] `python NOBS/scripts/check_resources.py` 全绿（改了 XAML 资源引用时必做）
- [ ] `OBS_SELFTEST=1` 无界面自检全 PASS（改了路由 / 页面 / 新手引导时必做）
- [ ] 手工验证步骤（写清点了哪里、期望看到什么）：
  <!-- 例如：首页 → 简单录像 → 选「会议 / 网课」→ 开始 → 观察已录时长是否递增 -->

## 本项目的硬性约定

- [ ] **待写入用户 OBS 配置的功能**：已说明「先备份 / 写后读回校验 / 可回滚」三条路径，
      并且没有静默改写用户没同意过的参数
- [ ] **新增界面文案**：中英**成对**（`Localization/StringTableZhHans.cs` 与 `StringTableEnUs.cs`
      键集一致、英文无汉字与全角标点、占位符个数一致）
- [ ] **新增纯逻辑**：放在 `*Core.cs`（零 WPF 依赖）并配了 xUnit 单测；
      该文件已加入 `OBS_Helper.Wpf.Tests.csproj` 的 `<Compile Include>` 链接清单
- [ ] **没有新增第三方 NuGet 包**（主程序保持纯 BCL；测试工程的 xunit 除外）
- [ ] **双目标可编译**：`net10.0-windows` 与 `net6.0-windows` 都能构建
      （没有使用 .NET 7+ 独有 API，需要 polyfill 时走 `Services/Compat`）
- [ ] **改了路由 / 页面**：已同步 `Navigation/Routes.cs`、`MainWindow` 的 `_meta` 与导航项、
      以及 `MainWindow.RunSelfTestAsync` 的路由清单

## 关联

<!-- 关闭的 Issue：Closes #123 -->
