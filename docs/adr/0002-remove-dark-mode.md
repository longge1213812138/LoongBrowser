# 移除暗色模式

Vista 风格外壳没有暗色形态，暗色模式与本次视觉目标直接冲突。移除 `DarkModeManager` 的界面配色职责、`TabStrip` 的主题分支、`MainForm` 的「暗色模式」菜单项，以及 `NewTabPage` 的第二套 CSS。

## 后果

`darkmode.json` 中的旧值失效；网页不再被强制注入暗色 CSS。这不是配置项默认值的变化，是功能删除。
