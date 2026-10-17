# 用 Acrylic 实现玻璃效果，而非复刻 Aero Glass

视觉目标是 Windows Vista 风格的毛玻璃外壳，但 `DwmExtendFrameIntoClientArea` 那套 Aero Glass 在 Windows 10 1903+ 已对顶层窗口失效。改用 Acrylic 玻璃，得到机制不同但视觉接近的效果。

## 实测：用哪套 API

在本机（Windows 11 25H2，Build 26200）用高对比条纹背景逐配方实测：

| 配方 | 结果 |
| --- | --- |
| `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND` | 返回 `hr=1`，但窗口内**无任何模糊**，条纹完全不透 |
| `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` + `DwmExtendFrameIntoClientArea(-1)` | **条纹被真实模糊**，Acrylic 生效 |
| 不做任何调用（对照） | 条纹锐利，基线 |

结论：**`SetWindowCompositionAttribute` 的返回值不能作为生效证据**，它在 Win11 25H2 上返回成功却无效果。实际生效路径是 Win11 22H2+ 的 `DWMWA_SYSTEMBACKDROP_TYPE`（38），且必须配合 `DwmExtendFrameIntoClientArea` 全边距 `-1`。

## 后果

- 最低支持系统抬到 Windows 11 22H2（Build 22621）。Win10 与 Win11 21H2 及更早不呈现玻璃。
- 不做运行时探测与降级开关：调用失败或系统不满足时窗口保持不透明外观，不改变布局、不写配置、不提供用户开关。
- 窗口必须设 `ControlStyles.Opaque` 且**不绘制背景**，否则 GDI 内容会盖住合成层的模糊。
