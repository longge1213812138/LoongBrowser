# 无边框窗口的边缘缩放：自实现，不用 WS_THICKFRAME

> **2026-10-19 更新**：本文关于 `WS_THICKFRAME` 会让失焦丢玻璃的结论已被复测确认，
> 仍然成立；但「因此不做 Aero Snap」这一取舍已被 **ADR-0010** 取代 ——
> 现在改由 `SnapController` 自己实现贴边分屏，两者兼得。下文保留作为历史记录。

窗口是 `FormBorderStyle.None`（自绘标题栏的前提），系统因此不提供任何可拖拽的缩放边框。需要补回"拖四边/四角自由改变大小"。

## 结论先行

**不能用系统缩放。** 要用系统缩放必须补 `WS_THICKFRAME`，而这一位会让 DWM 在窗口**失活**时停用 Acrylic —— 失焦后整个窗口变成不透明纯色块，玻璃彻底消失，且重新 `Apply`、强制重绘、重设边框都救不回来。用户要求"玻璃常驻、不做降级"，所以缩放改由程序自己实现。

## 实测：WS_THICKFRAME 与失焦的关系

用"彩色条纹背板 + 像素饱和度"做判据（玻璃生效时外壳带必然带出背板颜色，失焦后变成纯灰）。二分法逐个加配置：

| 窗口配置 | 前台 sat | 失焦 sat |
| --- | --- | --- |
| 只有 Acrylic + 每帧清面 | 93 | **93（玻璃常驻）** |
| 上一条 **+ WS_THICKFRAME** | 93 | **0（玻璃消失）** |
| + 子控件覆盖（无 THICKFRAME） | 255（白底，非玻璃） | 255 |
| + Padding（无 THICKFRAME） | 211 | 211 |

只加 `WS_THICKFRAME` 这一位就足以复现。恢复手段全部无效：重新 `Apply`、`DwmExtendFrameIntoClientArea` 重设、`DwmFlush`、`RedrawWindow`、移动 1px、隐藏再显示 —— 失焦后 sat 始终为 0。

同一套判据下，去掉 `WS_THICKFRAME` 的窗口：前台 93、失焦 93、回前台 93。

## 做法

- 边缘一圈 6px（`VistaTheme.ResizeBorderWidth`）由 `MainForm.Padding` 让出，属于 Form 自己；
- Form 在边缘带按下鼠标即进入拖拽态（`WindowResizer.DragSession`），按屏幕位移直接改 `Bounds`；
- 边带同时就是玻璃：Form 只清面不铺色，Acrylic 从这圈透出，只补两条 1px 边缘线；
- 最小尺寸由 `MinimumSize` 在拖拽时钳制（钳住后继续拖，边不再跟着走）。

## 后果

- **不做 Windows 的 Aero Snap（拖到屏幕边缘半屏吸附）**：那需要系统缩放循环，与玻璃常驻互斥。这是有意的取舍。
- 边缘缩放完全在托管代码里，`WM_NCHITTEST` / `WM_NCCALCSIZE` / `WM_GETMINMAXINFO` 一整套系统缩放路径都不再需要，相关补丁（最大化溢出、还原多 14px）随之删除。
- 这条约束由 `tests\run_glass_persist_test.ps1` 锁住：断言窗口样式**不含** `WS_THICKFRAME`，同时断言边缘缩放仍然可用。
