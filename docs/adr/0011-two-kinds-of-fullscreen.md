# ADR-0011：两套全屏 —— 视频真全屏与网页全屏分开

- 状态：已采纳
- 日期：2026-10-19
- 相关：ADR-0007（缩放边带）、ADR-0010（自实现 Aero Snap）

## 背景

用户报告：**视频全屏播放有边框**；并且要求“视频真全屏”与“网页全屏”**分开，做两套方案**。

复现（B 站视频页，`<video>.requestFullscreen()`，CDP 带 `userGesture`）：

```
Form.Bounds             = {0,0,2560,1440}     ← 窗口已经铺满整屏
Form.Padding            = {6,6,6,6}           ← 但内边距还在
WebView2 client(screen) = {6,6,2548,1428}     ← 视频少了 12x12
```

## 根因

`MainForm` 为了给无边框窗口补回缩放热区，在构造时设了
`Padding = new Padding(VistaTheme.ResizeBorderWidth)`（6px），所有子控件都缩在这圈里
（见 ADR-0007）。真全屏时窗口确实铺满了整屏，但**这圈 Padding 没有归零**，
于是 `Dock.Fill` 的 WebView2 只能拿到 `2548x1428`，四周露出 6px 的玻璃，
`MainForm.OnPaint` 还在上面画了 1px 白高光 —— 看起来就是“视频四周有一圈边框”。

## 决定

拆成两套，语义、触发者、可见范围都不同：

| | 视频真全屏（`FullscreenKind.Video`） | 网页全屏（`FullscreenKind.Page`） |
|---|---|---|
| 触发 | 网页调 Fullscreen API（播放器「全屏」按钮） | `F11` / 「查看 → 网页全屏」 |
| `Padding` | 归零 | 归零 |
| WebView2 | 从 TabPage **摘出**，直接挂 Form | 同样**摘出**挂 Form（理由见下，引用计数共用） |
| 自绘标题栏 | 隐藏 | **保留**（还能拖动/最大化/关闭） |
| 菜单/工具栏/地址栏/标签头 | 隐藏 | 隐藏 |
| 窗口范围 | `Screen.Bounds`（**含任务栏**） | 最大化到 `WorkingArea`（**任务栏可见**） |
| 置顶 | 是 | 否 |
| 退出 | `Esc` / `F11` / 网页自己退 | `F11` / 菜单 |

状态是**推导**出来的，不额外存一份：`Kind` 读 `_videoActive` 与各窗口的 `Active`，
优先级 `Video > Page > None`。存一份就会漂移 —— 从视频全屏退出后网页全屏其实还在。

两套**可叠加**：网页全屏中进视频全屏，视频全屏按当时的可见性（外壳已隐藏）保存，
退出后网页全屏原样保留。`run_fullscreen_test.ps1` 的 D 段锁着这条。

## 网页全屏为什么也得搬（这一版改了主意）

最初的判断是「网页全屏只让外壳退场，WebView2 留在 TabPage」——理由是标签头早被
`TabStrip.HideHeader` 压成 0，视觉上的标签条是 `GlassChrome` 画的，藏掉 Chrome
就等于藏掉标签栏，搬控件没有收益。

实测推翻了这个判断。只藏外壳时：

```
webview = {4, 35, 2552, 1353}      工作区 = 2560x1392
          ↑  ↑   ↑      ↑
          |  |   宽度少 8（TabControl 自带 4px 边框，左右各一）
          |  顶部少 35（TabControl 边框 + 标签头残留高度）
          `TabControl` 的客户区左沿
```

也就是说还会剩一圈边。改成一并抬到 Form 上之后：

```
webview = {0, 30, 2560, 1362}      标题栏之下铺满客户区
```

**两套共用同一套"抬起来"的机制**（`HostView` / `UnhostView`）并带**引用计数**：
两套可以叠加，先进网页全屏再进视频全屏时，第二次不能再"记录原位置"（那时原位置
已经是 Form 了），退出时也只该由最后一次退出负责放回 —— 否则控件会被放到 Form 上
再也回不去。`run_fullscreen_test.ps1` 的 C9b / D 段锁着这条。

站点自己的「网页全屏」（B 站的 CSS 变换）在此之上叠加：播放器撑满 WebView2，
也就撑满整个客户区。两者天然协同。

## 顺带修掉的真实缺陷

写 C10 断言“最大化不盖任务栏”时失败：

```
窗口 = {0,0,2560,1440}   工作区 = {0,0,2560,1392}
```

`FormBorderStyle.None` 让窗口失去了系统的最大化边界约束 —— 系统按“有边框窗口”算最大化矩形，
给标题栏与边框留了补偿，我们没有那些非客户区，补偿落空，窗口就长到整块屏幕。
之前 `run_snap_test` 的 C1 用 `>=` 断言（宽松），把这个缺陷放过去了。

修法用标准做法：处理 `WM_GETMINMAXINFO`，把 `ptMaxSize` / `ptMaxPosition`
钉在工作区上，任务栏与多显示器仍交给系统算。

## 影响

- `FullscreenManager` 新增 `FullscreenKind` 枚举、`Kind` / `IsPageFullscreen` / `IsAnyFullscreen` / `ExitAll`；
  `IsFullscreen` 语义收窄为“视频真全屏”（此前就是它，未改变）。
  `IsUiFullscreen` 保留为 `IsPageFullscreen` 的兼容别名。
- `MainForm.WndProc` 增加 `WM_GETMINMAXINFO`；`SystemHitTest` 在**任意**全屏下都不再返回
  缩放边或 `HTCAPTION`（视频全屏里拖边会把视频缩回去）。
- `GlassChrome` 新增 `MenuOpening` 事件：全屏可能从网页里触发，宿主收不到点击，
  菜单勾选在**展开前**重算，不依赖事件推送。
- 「查看」菜单新增「网页全屏」项（`F11`），视频真全屏时置灰 —— 那一套由网页自己管。

## 未覆盖

- 多显示器：视频全屏固定用 `Screen.FromControl(form)` 那一块屏，不跨屏。
- 全屏时若用户在网页里把浏览器切到后台，玻璃仍按 ADR-0010 的规则保持（不涉及新增样式位）。
