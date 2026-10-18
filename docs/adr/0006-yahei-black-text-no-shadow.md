# 外壳文字用微软雅黑、纯黑、不加阴影

外壳文字统一使用系统自带的微软雅黑（`Microsoft YaHei UI` → `Microsoft YaHei` → `Segoe UI` 依序取第一个可用族），主文字纯黑 `#000000`，不绘制任何阴影。自绘路径（标题栏标题、菜单栏项、标签标题、下拉菜单）与 WebView2 新标签页页面统一到这套取值；6 个原生窗体（含下拉面板 `ChromeDropDown`）与地址栏 `TextBox` 只设字体与前景色。

## 取值（集中在 `VistaTheme`）

| 项 | 值 | 原值 |
| --- | --- | --- |
| 字体族 | `Families = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" }` | 同左（本项目一直用系统雅黑） |
| 常规字号 | `UIFont` 10pt | 9pt |
| 标题字号 | `VistaTheme.TitleFont` 10.5pt；`TabStrip.TitleSize` 11.5pt | 9.5pt / 10.5pt |
| 主字色 | `Text = Color.Black` | `#1C2530` |
| 文字阴影 | 无 | — |

字号三档各 +1pt 是给黑体时代留的余量，换回雅黑后沿用：10pt 雅黑与旧 9pt 相比只是略大，仍在各固定高度的富余范围内（标题栏 30px、菜单栏 26px、工具栏 36px、标签条 40px 均容纳得下）。

## 为什么不用黑体，也不内嵌字体

两次方向都试过，最终都否掉了：

- **黑体 `SimHei`**：中易黑体是商业字体，只能随 Windows 授权使用。直接在 `Font` 里写 `"SimHei"` 只是引用系统已装的字体、不构成分发，但 Windows 各版本渲染有差异；若为了统一观感而内嵌，就跨进了授权红线。
- **内嵌思源黑体（SIL OFL 1.1）**：授权上合法且跨机器一致，实测把 16.4 MB 的 OTF 子集化到 3.0 MB（GB2312 全字 + ASCII + 常用标点共 7558 字符）、用 `/resource` 嵌进 exe、`PrivateFontCollection.AddMemoryFont` 从内存加载，全部跑通。但 exe 从 121 KB 涨到 3.1 MB，与「极简、零第三方运行时依赖」的项目定位冲突，收益（用户机器上本来就有雅黑）覆盖不了代价。

结论：用系统雅黑。它随 Windows 分发、无需打包、不涉授权，是体积与一致性的最优解。

## 为什么不做文字阴影

曾按「玻璃区半透明白、白底区半透明黑」做过双极性 1px 阴影，实现上还迫使 `TextRenderer.DrawText` 换成 `Graphics.DrawString`——实测 `TextRenderer` 完全忽略 `foreColor` 的 alpha（以 `Color.FromArgb(80,0,0,0)` 绘制，输出像素 alpha 恒为 255），画不出半透明阴影。做完后判定：阴影在深色壁纸下救不回可读性，在浅色白卡片上又偏脏，观感收益不成立，整体移除。

于是外壳文字回到单层 `DrawString`，但仍保留 `VistaTheme.DrawChromeText` 作为统一入口——它负责 `TextFormatFlags` → `StringFormat` 映射、`&` 加速键前缀剥离、`TextRenderingHint` 设置，集中一处免得每个绘制点各写一遍。

## 换引擎踩到的坑：`&` 加速键前缀

`TextFormatFlags.NoPrefix` 在 `TextRenderer` 下是「原样显示 `&`」（不解释加速键），而 `DrawString` 完全不处理前缀，直接迁移会让加速键标记原样出现在外壳上（如「书签(&B)」）。修复是在 `VistaTheme` 加 `StripAmpersands`，`DrawChromeText`（flags 含 `NoPrefix` 时）、`Ellipsize`、`MeasureWidth` 统一先剥标记。以后往外壳加新文字，凡是依赖 `TextRenderer` 语义的地方（对齐、前缀、内边距）都要逐项核对。

## 度量必须同口径

绘制改用 GDI+ 后，测量也必须用 GDI+：`Ellipsize` 与 `MeasureWidth` 从 `TextRenderer.MeasureText`（GDI）改为 `g.MeasureString`，否则省略号位置与菜单项宽度会差出几个像素。

## 可回退：未选中标签不做灰阶区分

标签两态文字都用纯黑，选中态靠卡片底色（`#FFFFFF` / `#EEF2F7`）与冷蓝描边区分。这是用户的临时决定——「未选中标签不做灰阶区分，改完之后视情况以后再改」。`VistaTheme.TextMuted` 因此暂时没有引用，但保留未删；要恢复灰阶层级，把未选中标签的绘制颜色换回 `TextMuted` 即可。

## 与既有 ADR 的关系

本文不推翻 ADR-0002（移除暗色模式）与 ADR-0005（玻璃不染色），只是给外壳文字新增一层决定。0002 定下「只有一套配色」，本文定下这套配色里的文字是雅黑纯黑；0005 定下玻璃的观感由自绘层承担，而自绘层现在只有字、没有阴影。
