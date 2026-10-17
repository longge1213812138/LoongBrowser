# 用自绘外壳取代 WinForms 原生控件

WinForms 的 `MenuStrip` / `ToolStrip` 无法在半透明背景上正确绘制（透明 `BackColor` 会画成父窗口色），`FormBorderStyle.None` + 自绘标题栏是做出 Vista 式连续玻璃外壳的唯一路径。因此菜单栏、工具栏、地址栏、标签条全部并入单个自绘 `GlassChrome` 面板，窗口边框改为自绘。

## 后果

- Windows Snap、DPI 缩放、系统菜单（Alt+Space）等系统行为需手工移植。
- `FullscreenManager` 从「隐藏菜单 + 工具栏两个控件」改为「隐藏一条 chrome」。
- 工具栏按钮改用矢量路径绘制，不再使用 Unicode 字符。
