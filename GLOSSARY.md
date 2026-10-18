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

**Surface Clear**:
每个绘制帧开始时把整块绘制面擦回透明（`PatBlt(BLACKNESS)`），让玻璃重新透出；少了它，旧位置的外壳元素会留在窗口上。
_Avoid_: 清屏, 重绘, 刷新

**Glass Timing**:
玻璃必须在窗口可见之后才应用（`MainForm.OnShown`）；可见之前设的材质会被窗口创建的后续流程覆盖。
_Avoid_: 初始化玻璃, 启动玻璃

**Aero Glass**:
Vista/Win7 的原生组合器玻璃；本项目不实现，仅作视觉参照。
_Avoid_: 用它称呼本项目当前的玻璃效果

### 文字渲染

**Glass Text**:
直接画在玻璃上、背后没有不透明衬底的文字，例如标题栏标题与菜单栏项。玻璃区的观感随桌面壁纸变化，因此这类文字的可读性不能只依赖颜色深浅。
_Avoid_: 透明文字, 浮字

**TabLabel**:
标签头卡片上显示网页标题的文字；选中态与未选中态共用这同一个词，两者的区别在卡片底色而不在文字本身。
_Avoid_: 标签文字, 页签标题

**Chrome Text**:
外壳区域内一切由程序绘制的文字（标题栏标题、菜单栏项、标签标题、下拉菜单项）。统一用微软雅黑、纯黑、无阴影；它们是 Glass Text 与 TabLabel 的上位词，只在需要指代"整套外壳文字"时使用。
_Avoid_: 界面文字, UI 文本
