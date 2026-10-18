# 无边框窗口用 Padding 边带实现边缘缩放

窗口是 `FormBorderStyle.None`（自绘标题栏的前提），系统因此不提供任何可拖拽的缩放边框。需要补回"拖四边/四角自由改变大小"。

## 实测：三条路，只有一条通

在本机（Windows 11 25H2，Build 26200）用 `WM_NCHITTEST` 采样 + 真实鼠标拖拽逐条验证：

| 方案 | 结果 |
| --- | --- |
| 只让 Form 的 `WM_NCHITTEST` 返回 `HTLEFT` 等 | **无效**。不补 `WS_THICKFRAME` 时系统不认为窗口可缩放，返回了也没有反应 |
| 子控件返回 `HTLEFT` | **无效**。缩放循环由窗口的 `DefWindowProc` 发起，子控件给不出这个循环 |
| 子控件返回 `HTTRANSPARENT`，期望穿透到 Form | **无效**，六向全失败 |
| Form 留 `Padding`，边缘一圈属于 Form，Form 自己作答 `WM_NCHITTEST` | **六向全部成功** |

两条必须配套的修正：

- **补 `WS_THICKFRAME`**（`CreateParams`）。没有它系统不执行缩放。
- **同时把 `WM_NCCALCSIZE` 归零**。补了 `WS_THICKFRAME` 后系统会画出边框、把客户区往里挤 7px；归零后客户区重新等于整个窗口矩形。

## 后果

- 窗口四周有 6px（`VistaTheme.ResizeBorderWidth`）的边带，它同时是**缩放热区**和**玻璃**：`MainForm.OnPaint` 只清面不铺色，Acrylic 从这圈透出，只补两条 1px 边缘线。
- **内容控件必须缩在边带以内**（`MainForm.Padding`）。标题栏、玻璃外壳、标签容器都让出这 6px，否则鼠标永远落不到 Form 自己身上。
- **`WS_THICKFRAME` 会带来两处尺寸偏差**，都要自己兜：
  - 最大化时会按"带边框窗口"撑到比工作区大一圈 → 用 `WM_GETMINMAXINFO` 把最大化矩形与最大跟踪尺寸钉到监视器工作区。
  - "最大化 → 还原"时系统先给出正确矩形，紧接着再按带边框规则撑大 2×边框宽（实测 14px）→ 记下最大化前的矩形，还原后把它钉回去。
- **拖拽缩放时 `MinimumSize` 会失效**，因为 WinForms 是在 `Form.WndProc` 之后才填 `WM_GETMINMAXINFO` 的，那时 `ptMinTrackSize` 还是系统默认的 136×39（实测）。必须自己把 `MinimumSize` 写进 `ptMinTrackSize`。
- 最大化状态不返回缩放码：系统本来也不允许拖边改大小。

回归见 `tests\run_resize_test.ps1`（命中测试、样式、客户区、边带布局、最小尺寸、最大化）。
