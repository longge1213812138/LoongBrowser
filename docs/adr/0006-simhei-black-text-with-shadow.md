# 外壳文字统一黑体、纯黑与 1px 双极性阴影

外壳文字统一改用黑体 SimHei、主文字纯黑 `#000000`，并给每处文字加一层 1px 偏移的半透明阴影。自绘的四处——标题栏标题、菜单栏项、标签标题、下拉菜单——阴影画在 `Graphics` 上；新标签页是 WebView2 页面，同样的 1px 阴影由 CSS `text-shadow` 承担；6 个原生窗体（含下拉面板 `ChromeDropDown`）与地址栏 `TextBox` 只换字体与颜色，不加阴影。目的是让黑字在玻璃与浅色衬底上都能站住，同时保住 Vista 外壳「字有厚度」的观感。

## 取值（集中在 `VistaTheme`）

| 项 | 值 | 原值 |
| --- | --- | --- |
| 字体族 | `Families = { "SimHei", "Microsoft YaHei UI", "Segoe UI" }` | 雅黑优先、无 SimHei |
| 常规字号 | `UIFont` 10pt | 9pt |
| 标题字号 | `VistaTheme.TitleFont` 10.5pt；`TabStrip.TitleSize` 11.5pt | 9.5pt / 10.5pt |
| 主字色 | `Text = Color.Black` | `#1C2530` |
| 阴影偏移 | `ShadowOffset = 1`（向下一像素） | — |
| 玻璃区阴影 | `ShadowOnGlass = Color.FromArgb(140, 255, 255, 255)`，白、约 0.55 | — |
| 浅底区阴影 | `ShadowOnLightSurface = Color.FromArgb(46, 0, 0, 0)`，黑、约 0.18 | — |

字号是三档各 +1pt：WinForms 的 `Font` 只认单一字体族、没有 CSS 那样的 fallback 链，黑体的字面又比雅黑小，同字号会显得瘦小，因此靠上调字号补偿，族缺失时才落到雅黑与 Segoe UI。`VistaTheme.TitleFont` 当前没有调用点（外壳标题统一用 `UIFont`，标签条用它自己的 `TabStrip.TitleSize`），作为标题层级的取值保留。

原生 WinForms 窗体共 6 个，构造里各加一行 `Font = VistaTheme.UIFont`：`ChromeDropDown`、`DownloadDialog`、`HistoryDialog`、`BookmarkDialog`、`TranslationDialog`、`TranslationConfigDialog`；地址栏 `TextBox` 同理。它们**都不加阴影**——`ChromeDropDown` 虽是自绘，文字仍走 `DrawChromeText`，`Font` 只为度量一致。阴影只施加在 `DrawChromeText` 这一条路径上，这也是本文标题说「外壳文字」而非「所有文字」的原因。

## 阴影按衬底极化

`ShadowPolarity` 只有两档，判断依据是「文字下面有没有不透明衬底」，不是控件类型：玻璃区（标题栏标题、菜单栏项）背后没有衬底、桌面壁纸会透出，用半透明白色高光；不透明浅底区（标签卡片、下拉菜单、书签墙）用半透明黑。极性由每个绘制点在调用处显式声明，不做像素采样自动判断——采样会把绘制变成两次渲染，收益也不值得。

同一条标签文字有两个绘制点：生产路径是 `GlassChrome.DrawTabHeader`，`TabStrip.DrawItem` 只为测试预览图保留（见 ADR-0004）。两处都改用同一套参数，否则预览图与真实外壳对不上。新标签页是 WebView2 页面，阴影由 CSS `text-shadow:0 1px 1px rgba(0,0,0,.18)` 承担，与浅底区的 0.18 对齐。

## 关键取舍：`TextRenderer.DrawText` 换成 `Graphics.DrawString`

做不出半透明阴影是换 API 的唯一原因。实测让 `TextRenderer.DrawText` 以 `Color.FromArgb(80, 0, 0, 0)` 绘制，输出像素的 alpha 恒为 255——它整条路径忽略 `foreColor` 的 alpha，半透明阴影根本不会出现。因此新增 `VistaTheme.DrawChromeText`：先按极性取阴影色，在 `(0, +1)` 偏移处用 `Graphics.DrawString` 画一遍，再在同一位置画主字，两层都是 GDI+ 的半透明 `SolidBrush`，会真正与底层混色。（实测像素记录见 `tests/shadow_probe.png`；若该图未随仓库保存，以 `src/VistaTheme.cs` 里 `DrawChromeText` 的 remarks 为准。）

绘制换了引擎，度量必须跟着换：`Ellipsize` 与 `MeasureWidth` 从 `TextRenderer.MeasureText`（GDI）改成 `g.MeasureString`（GDI+），否则省略号位置与菜单项宽度会差出几个像素。

替换范围是 6 处调用点（HEAD 里共 7 处 `TextRenderer.DrawText`）：标题栏标题、菜单栏项、标签头标题、`TabStrip.DrawItem`、下拉菜单项与快捷键——即 `TitleBar` 1 处、`GlassChrome` 2 处、`TabStrip` 1 处、`ChromeMenu` 2 处。标签上的关闭按钮 × 没有换：它是图标不是文字，不加阴影，继续用 `TextRenderer` 保持原有定位。

GDI 换 GDI+ 不是纯 API 替换，语义要逐项补。已踩到的一个坑：`TextFormatFlags.NoPrefix` 在 `TextRenderer` 下是「原样显示 &」（不解释加速键），而 `DrawString` 完全不处理前缀，直接迁移会让加速键标记原样出现在外壳上（如「书签(&B)」）。修复是在 `VistaTheme` 加 `StripAmpersands`，`DrawChromeText`（flags 含 `NoPrefix` 时）、`Ellipsize`、`MeasureWidth` 统一先剥标记。以后往外壳加新文字，凡是依赖 `TextRenderer` 语义的地方（对齐、前缀、内边距）都要逐项核对。

## 已知代价：玻璃上的黑字

不推翻 ADR-0005：玻璃仍然不染色，透出的是桌面壁纸。壁纸偏深时纯黑文字与背景亮度接近，可读性下降；1px 半透明白高光只能勾出轮廓、缓解观感，不能根治。这是用户明确选择的取舍——宁可接受深色壁纸下偏糊，也要浅色壁纸上黑字的锐利。阴影颜色全部收在 `VistaTheme` 的两个常量里，以后要调只改一处。

本项目未做 DPI 感知（无 manifest、无 `AutoScaleMode`），`ShadowOffset = 1` 是 96 DPI 下的设计值。将来若引入 DPI 适配，只需让这一处随 DPI 缩放。

## 可回退：未选中标签不做灰阶区分

标签两态文字都用纯黑，选中态靠卡片底色（`#FFFFFF` / `#EEF2F7`）区分。这是用户的临时决定——「未选中标签不做灰阶区分，改完之后视情况以后再改」。`VistaTheme.TextMuted` 因此暂时没有引用，但保留未删；要恢复灰阶层级，把未选中标签的绘制颜色换回 `TextMuted` 即可。

## 考虑过的替代方案

- **不换 API，用 `TextRenderer` + 不透明阴影**：少改 6 处调用点，但阴影完全不透明，在白卡片上像描边、在玻璃上像脏边，不是 Vista 的观感。
- **只描边不偏移**：黑字四周包一圈会显得更粗更钝，与 Vista 的轻字重不符。

## 与既有 ADR 的关系

本文不推翻 ADR-0002（移除暗色模式）与 ADR-0005（玻璃不染色），只是给外壳文字新增一层决定。0002 定下「只有一套配色」，本文定下这套配色里的文字是黑体字配双极性阴影；0005 定下玻璃的观感由自绘层承担，阴影正是这层自绘的一部分。
