# 保留 TabControl 作为内容容器，标签头改为自绘

`TabControl` 的标签条是控件自身的非客户区，WinForms 无法把它拆到另一个面板。而视觉目标要求标签条落在 `GlassChrome` 玻璃里。因此保留 `TabControl` 只承载 `TabPage`/`WebView2`，用 `Appearance=FlatButtons` + `ItemSize=(0,1)` 把标签头压到最小（实测残留约 5px，无法归零），标签的视觉与命中区域全部由 `GlassChrome` 自绘。

## 考虑过的替代方案

- **彻底弃用 `TabControl`，改 `Panel` + 自绘标签头**：视觉最纯粹，但要改动 7 个测试文件里约 40 处 `TabManager(_tabs)` / `_tabs.TabPages` 引用，以及 `FullscreenManager` 的 29 处引用（真全屏要把 WebView2 从 `TabPage` 摘到 `Form` 上再放回）。测试改动量约等于生产代码改动量，而收益只是省掉一个 5px 隐形头。

## 后果

- `TabControl` 保留约 5px 的不可见标签头（`ItemSize` 高度设 1 后实测 `DisplayRectangle.Y=5`，无法压到 0），需要处理 z-order 与焦点。
- `TabStripRenderTest` 中断言 `Style()` 效果的用例（`ItemSize`、`DrawMode`、`SizeMode`）不再适用，需改写为断言新的标签头渲染。
- `FullscreenManager` 不动。
