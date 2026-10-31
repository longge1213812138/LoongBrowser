# ADR-0012：网页右键菜单改用自绘 ChromeDropDown

- 状态：已采纳
- 日期：2026-11-01
- 相关：ADR-0003（自绘窗口外壳）、ADR-0009（退出判据与单一登记）

## 背景

用户要求：右键点击网页中的链接，能「在新窗口中打开」，打开后是两个独立的浏览器窗口。

实际右键后发现弹出的不是本程序的中文菜单，而是 **WebView2 内核的默认英文菜单**
（Open link in new window / Copy link address / Inspect）。
自定义菜单里的「在新窗口中打开链接」自然也就无从点起。

## 根因（三处叠加）

| # | 根因 | 后果 |
|---|---|---|
| 1 | `ContextMenuTarget` 的属性读取都是 COM 调用，在某些场景抛 `0x8000000E` | 异常逃出 `BuildContextMenu`，`e.Handled = true` 执行不到 → 退回内核默认菜单 |
| 2 | 旧实现只往 `e.MenuItems` 塞数据，**从不显示菜单** | `e.Handled = true` 的语义是「我来处理这个菜单」，不是「我会显示菜单」 |
| 3 | 菜单命令回调在 WebView2 上下文里直接开窗口 | 在错误的线程上下文操作窗口 |

第 1 条由 `AppDomain.FirstChanceException` 探针捕获到
`COMException (0x8000000E) ... at CoreWebView2ContextMenuTarget.get_SelectionText()`。

## 决定

1. **COM 属性逐个安全读取**：`SafeTargetString` / `SafeIsEditable` 捕获异常，
   读不到就当没有，菜单照常构建 —— `e.Handled = true` 必须能执行到。
2. **菜单改由自绘 `ChromeDropDown` 显示**：与外壳菜单栏同源、同字体、同配色，
   `OnDeactivate` 自动收起；不再依赖内核的 `e.MenuItems` 渲染。
3. **坐标必须换算**：`e.Location` 是 WebView2 客户区坐标（原点为网页区域左上角），
   不是屏幕坐标；直接传给 `ShowAt` 会把菜单弹到屏幕左上角。
   一律经 `view.PointToScreen` 换算。
4. **新窗口走延迟执行**：`ScheduleNewWindow` → 定时器 → `Program.OpenNewWindow`，
   与 `ScheduleNewTab` 同一机制，确保离开 WebView2 回调上下文再开窗口。

## 后果

- 右键后弹出与外壳一致的中文菜单，出现在鼠标旁。
- 「在新窗口中打开链接」会保留原窗口，另开一个独立 `MainForm`（进程内多窗口，
  共享同一个内核环境，不争抢用户数据目录）；两个窗口互不影响。
- `Program.OpenNewWindow` 仍是唯一入口，`LoongBrowserContext` 继续自己记账窗口数
  （见 ADR-0009），关闭其一不影响另一窗，最后一个浏览器窗口关闭才退出进程。

## 验证

`tests\run_context_new_window_e2e.ps1`（12 项）：真实 `MainForm` + 真实 WebView2 +
`SendInput` 真实右键与真实左键点选；菜单项坐标从 `ChromeDropDown` 的实际布局
（`_items` / `_heights`）算出，不靠猜。
