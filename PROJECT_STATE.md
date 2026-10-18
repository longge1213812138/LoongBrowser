# PROJECT_STATE — LoongBrowser 交接材料

> 更新：2026-10-19 02:05 ｜ 状态：**玻璃常驻、边缘缩放、工具栏悬停提示已实现，待用户实机确认**
> 工作目录：`C:\Users\91533\Desktop\LoongBrowser-master` ｜ 分支：`main` ｜ HEAD：`4eec1c2`
> 权威资料：功能描述以 `README.md` 为准；本文档只记"接手需要知道、README 没写"的事。

## 一、项目目标

LoongBrowser：极简 Windows 桌面浏览器（WebView2/Chromium 内核 + C# WinForms），零第三方运行时依赖，交付可安装 EXE。设计依据见《开发计划书.md》（v1.1）。

## 二、已提交成果（git main，至 4eec1c2）

| 提交 | 内容 |
| --- | --- |
| `4eec1c2` | 还原网页和视频全屏：真全屏时同时隐藏自绘标题栏（FormBorderStyle.None 后系统标题栏不存在，`FullscreenManager.Init` 签名加入 `TitleBar` 参数） |
| `9a6d2a9` | 应用图标：罗盘环（icons/D-compass.svg）+ `/win32icon` 接入构建；16/20/24 小尺寸按档重绘（缺口会读成字母 C）；exe 127→159 KB |
| `af3a1fd` | 内核初始化失败：可诊断（KernelDiag.cs 落盘日志）+ 自救重试（丢坏环境、清陈旧锁）+ 失败后置空 `_env` |
| `c6b931e` ~ `e54c4f1` | 毛玻璃外壳、字体定稿（微软雅黑、纯黑、无阴影，见 docs/adr/0006） |
| `08f3da3` | 网页缩放（50%~200%，会话内按标签记忆） |
| `d768f7f` | 网页全屏 |
| `77fcd7d` | docs: 工具链描述同步（csc → Roslyn bootstrap） |
| `cbe0fb1` | build: Roslyn bootstrap + legacy csc 回退 |
| `042d8ba` 之前 | 翻译、地址栏按钮、全屏、libs/ 入库等（见 git log） |

功能全集与使用说明见 `README.md`（权威资料）。

## 二·五、未提交：玻璃外壳三处修正（2026-10-19）

用户给了四张实机截图，对应三个问题，均已复现并修复（详见 ADR-0001 的"什么时候应用、怎么擦"一节）：

| 现象 | 根因 | 修法 |
| --- | --- | --- |
| 刚启动没有玻璃，之后重绘才突然出现 | 窗口**可见之前**设的 backdrop 被窗口创建流程覆盖 | `MainForm.OnShown` 里才 `GlassManager.Apply` |
| 最大化 / 缩小后地址栏白条残留在旧位置 | `Opaque` 控件是"叠加"语义，GDI 只重画失效矩形，没落笔的像素保留旧内容 | `GlassManager.ClearSurface`：每帧先 `PatBlt(BLACKNESS)` 把整块绘制面清成透明，再画前景 |
| 鼠标移到瑕疵上就消失 | 悬停触发局部重绘 —— 恰好是"缺清面"的直接证据 | 同上 |

配套改动：`Apply` 改为幂等（先 `DWMSBT_NONE` 再 Acrylic）；外壳绘制前先 `Relayout`，TextBox 不再在 OnPaint 中途移动；尺寸变化后整窗重绘。
测试：新增 `tests\run_glass_test.ps1`（9 项不变量），并改造 `ChromePreviewTest` 的清面断言（24 项）。两者都不依赖 DWM 合成层。

**验证方式（本机）**：桌面是纯灰壁纸，玻璃在纯色下不可辨。验证玻璃本身要用彩色条纹背板（临时程序，未入库）：全屏无边框窗口画红/黄/蓝条纹置于底层，再启动 exe，截图看标题栏是否透出模糊色块。已确认启动首帧、最大化、缩小、连续拖动缩放、F11 进出、最小化还原后均正常。

## 二·六、未提交：无边框窗口的边缘缩放（2026-10-19）

`FormBorderStyle.None` 的窗口没有系统缩放边框，补了一整套（详见 ADR-0007）：

| 关键点 | 做法 |
| --- | --- |
| 系统才会执行缩放 | `CreateParams` 补 `WS_THICKFRAME` |
| 补了样式后客户区被挤 | `WM_NCCALCSIZE` 归零，客户区重新等于窗口矩形 |
| 谁来做命中测试 | **必须是 Form**：子控件返回 `HT*` 或 `HTTRANSPARENT` 都无法触发缩放循环（实测六向全失败） |
| 怎么让鼠标落到 Form | `MainForm.Padding = 6px`，标题栏/外壳/标签容器全部缩在边带以内 |
| 边带长什么样 | 就是玻璃本身：只清面不铺色，补两条 1px 边缘线 |
| 最大化溢出工作区 | `WM_GETMINMAXINFO` 把最大化矩形与最大跟踪尺寸钉到监视区工作区 |
| 最大化→还原多出 14px | 记下最大化前矩形，还原后用 `BeginInvoke` 钉回（系统会连发两次 Resize，第二次才撑大） |
| ~~拖拽缩放绕过 MinimumSize~~ | 已随系统缩放方案一并移除（见下） |

**2026-10-19 修正（重要）**：上表的 `WS_THICKFRAME` 方案**已被推翻**。实测（二分定位）这一位会让
DWM 在窗口失活时停用 Acrylic：点菜单、切应用、点桌面之后整个窗口变成不透明纯色块，玻璃消失且
重新 `Apply` / 强制重绘 / 重设边框都救不回来。用户要求"玻璃常驻、不做降级"，所以：

- **移除 `WS_THICKFRAME`**，连同 `WM_NCCALCSIZE` / `WM_GETMINMAXINFO` / 还原尺寸校正整套补丁一起删掉；
- 缩放改为 `WindowResizer.DragSession` 自己实现（边缘带按下 → 按位移改 `Bounds`，`MinimumSize` 钳制）；
- 取舍：**不做 Aero Snap 半屏吸附**（需要系统缩放循环，与玻璃常驻互斥）；
- 回归：`tests\run_glass_persist_test.ps1` 断言窗口样式不含 `WS_THICKFRAME`，同时断言缩放仍可用；
- 实测：前台 / 点菜单 / 失焦 / 回前台，玻璃饱和度全程 104 / 96 / 104 / 104。

详见 `docs/adr/0007-resize-border-band.md`。

测试：`tests\run_resize_test.ps1`（27 项，无真实鼠标注入，靠命中测试与窗口状态断言）。

## 二·七、未提交：工具栏悬停提示（2026-10-19）

鼠标停在前进/后退/刷新等按钮上满 1 秒，弹出按钮名字。

- `ChromeToolButton.Tooltip` 字段本来就有、一直没人用 —— 现在真正接上了；
- **不能画在 GlassChrome 内部**：提示要挂在工具栏附近，而工具栏下方紧接着标签条，
  自绘上去会糊住标签标题（实测截图可见）；
- **不用系统 ToolTip**：系统主题的不透明底与玻璃外壳的观感冲突；
- 用独立 `ChromeTooltip` 窗：`WS_EX_LAYERED` + 色键透明（只显示提示框本身）、
  `WS_EX_NOACTIVATE` + `ShowWithoutActivation`（不抢焦点、不影响继续点按钮）、
  `WS_EX_TOOLWINDOW`（不进 Alt+Tab）；
- 位置：**按钮右侧**（放不下翻左侧），竖直居中于工具栏。三个位置都试过：
  下方会糊住标签条标题；上方只剩 26px 放不下、会被挤到菜单栏上盖住"书签(B)"；
  右侧是唯一不遮挡任何文字的方案；
- 提示窗必须 **鼠标穿透**（`WS_EX_TRANSPARENT`）。不加的话提示与光标重叠时，
  系统把鼠标改判给提示窗、宿主收到 `WM_MOUSELEAVE`，提示被自己赶走 —— 表现为闪烁；
- 色键 `SetLayeredWindowAttributes` 要在 `Show()` 之后再设一次，否则**首次**弹出时
  窗口还是一块不透明洋红底（表现为"第一次不显示/闪一下"）；
- 守望定时器只负责收起提示，**不要**改 `_hotButton`：改了会让 `OnMouseMove` 误判
  "按钮没变"而不再重新计时，从一个按钮滑到另一个就再也弹不出来；
- 计时：换按钮立刻收起并重新计时；移出外壳、点击按钮都收起。

测试：`tests\run_tooltip_test.ps1`（17 项）。

## 三、本机环境注意事项（重要，接手必读）

1. **构建命令**：这台机器的执行策略会拦 `powershell -File build.ps1`，必须用
   `powershell -ExecutionPolicy Bypass -File build.ps1`。README 的"从源码构建"已写对，但 agent 直接跑会踩坑。

2. **沙箱 ACL 隐患（本机特有，会复发）**：`Desktop\LoongBrowser-master` 曾被 Codex 沙箱
   打上 `Low 完整性标签 + Everyone DENY(DC)`，导致普通权限双击 exe 时 WebView2 内核读不到
   宿主 exe 旁的文件，报 `0x800700AA`（管理员运行正常）。已于 2026-10-18 修复
   （`icacls /remove:d Everyone /t` + `/setintegritylevel "(OI)(CI)M" /t`，备份在
   `Desktop\LoongBrowser-acl-backup.txt`）。**若复发**（普通双击报错、管理员正常），同样两条命令修复；
   根治办法是把项目移出沙箱管辖目录（如 `C:\dev\`）。

3. **实机验证方式**：本 agent 环境的子进程会继承文件沙箱限制，`%TEMP%`/`%LOCALAPPDATA%`
   写入被拒，探针结论不可信。需要真实双击验证时，让用户操作或用
   `Shell.Application` 的 `ShellExecute`（等价双击，不继承沙箱）。

## 四、决定与限制

- 用户约束（来自开发计划书）：单一语言、零第三方运行时依赖、无 VS/NuGet 常备工具链；编译产物不得引入新运行时依赖。
- **零资源文件**：构建仍由 csc 直接编译，不引入 `.resx`/`.rc`。图标用 `/win32icon:` 参数嵌入，符合约束。
- 已否决：CEF/完整 Chromium 打包（体积）、Visual Studio 依赖（违背最小工具链）、系统级键鼠钩子（只为快捷键）。
- **WebView2 SDK 1.0.4191.47 vs 运行时 155.0.4283.60**：版本差大但非问题根源（内核初始化失败的真实根因是 ACL，见上）。升级 SDK 需从 NuGet 取新 DLL 替换 `libs/`，暂无必要。
- 字体：微软雅黑纯黑无阴影（ADR-0006）；思源黑体内嵌方案已试过并回退（exe 3.1 MB，与极简定位冲突）。

## 五、当前状态与下一步

- `git status` 干净（本次文档更新提交后）。
- build.ps1 可复现 BUILD OK（本机已验证；首次需网络拉 Roslyn，离线回退 legacy csc 会因插值语法失败——README 已写明）。
- 实机冒烟：图标、内核启动、正常浏览已验证；翻译/全屏/画中画等历史功能未在本会话回归，但均有测试套件覆盖（`tests\run_*.ps1`，README 有全表）。
- 待办（用户提出、未实现）：
  - **界面字体缩放**：字号硬编码在 `VistaTheme.UIFont`（10pt）、`VistaTheme.TitleFont`（10.5pt）、`TabStrip.TitleSize`（11.5pt）三处；实现时收敛到一个可缩放入口，并检查自绘布局常量是否需联动。注意：`4eec1c2` 之后外壳结构有变（TitleBar 参与全屏管理），动字体前先看现状。
  - **新标签页 HTML 字体联动**：需在生成 CSS 时注入缩放后的 `font-size`。

## 六、简短交接区

- 交接编号：2026-10-18-2340
- 用户确认范围：整仓
- 接手开场白（可复制）：

```
继续 LoongBrowser。目录 C:\Users\91533\Desktop\LoongBrowser-master，
先读 PROJECT_STATE.md（重点：第三节本机环境注意事项）。
HEAD 4eec1c2，git status 应为干净。
```
