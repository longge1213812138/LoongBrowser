# ADR-0009：退出判据只看"浏览器窗口"，文件关联只有一个实现

- 状态：已采纳
- 日期：2026-10-19

## 背景一：关闭窗口后进程残留

`LoongBrowserContext` 原先这样判断该不该退出：

```csharp
form.FormClosed += delegate {
    if (Application.OpenForms.Count == 0) ExitThread();
};
```

外壳的悬停提示 `ChromeTooltip` 是个独立 `Form`，悬停满 1 秒就会被创建，`Hide()` 只隐藏不销毁。
用户点任何工具按钮之前，鼠标必然先滑过它 —— 关窗时提示窗还在 `OpenForms` 里，`Count` 是 1，
`ExitThread()` 不执行。窗口消失了，消息循环还在转，进程存活并继续持有单实例互斥体，
下一次启动就被自己挡在门外。

**判据错了**：`OpenForms` 是"所有窗口"，而用户关心的是"还有没有浏览器窗口"。

## 决策

1. `LoongBrowserContext` 自己记账 `List<Form>`，最后一个**浏览器窗口**关闭即退出。
2. 关窗时 `GlassChrome.CloseTransientWindows()` 主动收掉提示窗与下拉菜单。
3. 辅助窗口（提示、下拉、消息框）永远不参与进程存亡判定。

## 背景二：「打开方式」里的多个 LoongBrowser

条目有三个来源：安装器写的 `Applications\LoongBrowser.exe`、主程序写的 ProgId，
以及 **Windows 自己**留下的 `<ext>_auto_file`（用"打开方式"选过一次就固化一条，
程序挪位置后就成了点不动的死条目）。三处各写各的，必然分叉。

## 决策

1. 新增 `FileAssociations` 作为唯一出口；`build.ps1` 把同一份源码也编进安装器。
2. 登记永远指向"当前正在运行的 exe"，并设置 `FriendlyAppName`。
3. 启动时静默清理"指向本程序且目标已不存在"的登记（含 `_auto_file`），只动自己的键。
4. 验收口径用 `SHAssocEnumHandlers`（Explorer 构建右键菜单时用的就是它），
   **不用自己拼的注册表视图**。

## 代价

- 退出逻辑多了一份窗口记账（十行），换来判据与用户认知一致。
- 每次启动扫一遍 `HKCU\Software\Classes` 顶层的 `*_auto_file` 与 `Applications`，
  几百个键，毫秒级；只读 + 按需删自己的失效项，不写别人的。
- 启动时改注册表这件事本身需要克制：因此范围严格限定为"本程序 + 目标不存在"，
  其它情形一律不碰，也不做后台常驻扫描。
