# 移除暗色模式

Vista 风格外壳没有暗色形态，暗色模式与本次视觉目标直接冲突。移除 `DarkModeManager` 的界面配色职责、`TabStrip` 的主题分支、`MainForm` 的「暗色模式」菜单项，以及 `NewTabPage` 的第二套 CSS。

## 执行结果

删除范围（切片 3 完成）：

| 位置 | 删除内容 |
| --- | --- |
| `src/DarkModeManager.cs` | 整个文件删除——状态与持久化、9 个界面颜色常量、`ApplyToForm`/`ApplyToControls`/`RefreshAllWindows`、`DarkMenuRenderer`、网页暗色 CSS 与注入脚本 |
| `src/TabStrip.cs` | 6 个 `IsEnabled` 三元分支、7 个暗色颜色常量、`DarkModeChanged` 订阅、关闭按钮的 `isDark` 判断 |
| `src/TabManager.cs` | `DarkModeChanged` 订阅、`OnDarkModeChanged`、`ApplyDarkModeCss` 及其在 `NavigationCompleted` 中的调用 |
| `src/NewTabPage.cs` | `@media (prefers-color-scheme:dark)` 整块 |

网页侧不再注入任何 CSS，页面按自身主题渲染。

## 后果

`darkmode.json` 中的旧值失效；网页不再被强制注入暗色 CSS。这不是配置项默认值的变化，是功能删除。已落盘的 `%APPDATA%\LoongBrowser\darkmode.json` 不做迁移也不删除，只是不再被读取。
