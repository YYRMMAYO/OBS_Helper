# UI 基线保存与 V3.5.0 全局背景规范

## 1. 重构前基线

- Git tag：`ui-baseline-v3.4.2`（指向 `248c2e0`，V3.4.2 最终 UI）
- 基线范围：
  - `OBS_Helper.Wpf/MainWindow.xaml(.cs)`
  - `OBS_Helper.Wpf/Themes/Palette.xaml`
  - `OBS_Helper.Wpf/Themes/Controls.xaml`
  - `OBS_Helper.Wpf/Themes/Icons.xaml`
  - `OBS_Helper.Wpf/Controls/WallpaperLayer.xaml(.cs)`
  - `OBS_Helper.Wpf/Services/AppearanceService.cs`
- 回滚方法：`git checkout ui-baseline-v3.4.2 -- NOBS/OBS_Helper.Wpf`（只回滚界面相关文件时按上面清单逐个取）。

## 2. 目标效果

参考用户提供的桌面聊天软件截图：**一张图片铺满整个窗口**，导航与内容面板变成半透明玻璃层，
文字与卡片仍然清晰可读。

技术要点：

1. `WallpaperLayer` 从内容区移到 `MainWindow` 根 `Grid`，跨两列；
2. 左侧导航与顶栏背景改为 `NavSurfaceBrush` / `TopBarSurfaceBrush`（带 alpha）；
3. 图片模式下 `SurfaceBrush` / `Surface2Brush` / `Surface3Brush` 使用 0xE6 / 0xD9 / 0xCC alpha，
   让卡片与输入框透出背景；默认 / 纯色模式下保持不透明；
4. 高对比模式强制不透明，直接回退背景层；
5. 背景层顺序：底色 → 图片（可模糊）→ 压暗 → 页面内容。

## 3. 可读性红线

- 正文文字与卡片底的对比度不得低于 4.5:1（大字号 3:1）。
- 图片模式默认压暗：浅色 15%，深色 40%；用户可调 0–80%。
- 模糊默认 0，用户可调到 24px；模糊值越大越要保证压暗。
- 减少动画开启时不做交叉淡入。

## 4. 落地清单

| 文件 | 变更 |
| --- | --- |
| `MainWindow.xaml` | 根 Grid 增加 `WallpaperLayer`；导航 / 顶栏改半透明刷子 |
| `AppearanceService.cs` | 图片模式下写入带 alpha 的 Surface 系列与 `Nav/TopBar` 资源 |
| `Palette.xaml` | 新增 `NavSurfaceBrush` / `TopBarSurfaceBrush` 默认值 |
| `WallpaperLayer` | 增加 `Scope` 文档说明（整窗），保留原有铺放 / 模糊 / 淡入逻辑 |
| `SettingsPage` | 背景说明改为「覆盖整个窗口」 |

## 5. 验收

- 默认主题、图片主题、深色、高对比四种组合都能正常启动；
- 图片模式下导航、顶栏、卡片可见背景但文字清晰；
- 高对比模式不显示壁纸；
- `OBS_SELFTEST=1` 不因资源键缺失失败。
