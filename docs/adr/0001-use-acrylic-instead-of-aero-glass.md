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

## 实测：什么时候应用、怎么擦

第一批玻璃落地后暴露了三个问题，用同样的条纹背板逐条复现并定位（2026-10）：

| 现象 | 根因 | 做法 |
| --- | --- | --- |
| 刚启动的一瞬间没有玻璃，之后某次重绘才"突然"出现 | 窗口**可见之前**设的 backdrop 会被窗口创建的后续流程覆盖 | Apply 移到 `MainForm.OnShown`（窗口已显示、首帧已画完） |
| 窗口缩放/最大化后，地址栏白条留在旧位置上，鼠标移上去也不消失 | `Opaque` + 不画背景是"往上叠加"而不是"每帧重来"：GDI 只重画失效矩形，没落笔的像素保留上一次内容 | 每个绘制帧先用 `PatBlt(BLACKNESS)` 把整块绘制面清成透明，再画前景（`GlassManager.ClearSurface`） |
| 鼠标移到瑕疵上，瑕疵闪一下就消失 | 悬停触发局部重绘，把那一小块重画了 —— 正是缺少清面的直接证据 | 同上；清面同时解决"只有鼠标经过的地方才恢复" |

两个关键细节：

- **必须临时放开裁剪区**。`OnPaint` 拿到的 `Graphics` 只裁在失效矩形上，只在那一块清面解决不了残留。
- **"擦回透明"只能靠 `PatBlt(BLACKNESS)`**。扩展边框后的客户区里，像素置 0 就等于"这里什么都没有"，DWM 会重新合成 Acrylic；GDI 无法直接写 alpha，别的填充色都只会留下新的不透明色块。
- **`Apply` 要可重复调用**。把材质设成与当前相同的值时 DWM 可能直接忽略，所以先设 `DWMSBT_NONE` 再设 `DWMSBT_TRANSIENTWINDOW`，句柄重建或从全屏还原后再调一次都能真正重设。

## 后果

- 最低支持系统抬到 Windows 11 22H2（Build 22621）。Win10 与 Win11 21H2 及更早不呈现玻璃。
- 不做运行时探测与降级开关：调用失败或系统不满足时窗口保持不透明外观，不改变布局、不写配置、不提供用户开关。
- 窗口必须设 `ControlStyles.Opaque` 且**不绘制背景**，否则 GDI 内容会盖住合成层的模糊；同时每个绘制帧都必须由控件自己清面（见上表）。
- 玻璃的时机与擦除行为由 `tests\run_glass_test.ps1`（不变量）与 `tests\run_chrome_preview.ps1`（离屏渲染）兜住，两者都不依赖 DWM 合成层。
