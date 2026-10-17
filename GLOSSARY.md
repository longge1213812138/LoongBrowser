# LoongBrowser

Windows 极简浏览器：WebView2/Chromium 内核 + C# WinForms 窗口外壳，零第三方运行时依赖。本文件固定本项目自有的词汇；通用编程概念不在此列。

## Language

### 窗口结构

**Chrome**:
主窗口中承载菜单、工具栏、地址栏与标签条的非网页区域。
_Avoid_: 界面框架, UI 骨架, 菜单栏（作为整段外壳的代称）

**Content**:
承载网页的 `_tabs` 区域，由 WebView2 渲染。
_Avoid_: 正文, 网页区, viewport

**GlassChrome**:
自绘的 Panel 子类，把菜单、工具栏、地址栏、标签条合并成的单一玻璃外壳。
_Avoid_: 玻璃面板, 顶部栏, toolbar

**TabHeader**:
`GlassChrome` 中自绘的标签条；标签的视觉与命中区域都在这里，与承载网页的 `Content` 分离。
_Avoid_: 标签头（指 TabControl 自带的那个）, tab strip, 标签栏

### 玻璃效果

**Glass**:
外壳区域透出并模糊桌面壁纸的半透明层。
_Avoid_: 毛玻璃, 磨砂, 透明

**Acrylic**:
本项目实际采用的玻璃实现：Win11 22H2+ 的 `DWMWA_SYSTEMBACKDROP_TYPE` 配合 `DwmExtendFrameIntoClientArea`。
_Avoid_: Vista 玻璃, DWM 模糊, blur behind, SetWindowCompositionAttribute

**Aero Glass**:
Vista/Win7 的原生组合器玻璃；本项目不实现，仅作视觉参照。
_Avoid_: 用它称呼本项目当前的玻璃效果
