# LoongBrowser

极简 Windows 桌面浏览器：**WebView2（Chromium 内核）+ C# WinForms**，单语言实现，零第三方运行时依赖，编译无需安装 Visual Studio / SDK。

```
主程序 ~159 KB · 依赖仅 WebView2 官方 SDK 3 个 DLL（约 880 KB） · 首次构建自动下载 Roslyn 编译器（约 100 KB），无需手工安装工具链
```

---

## 亮点

- **零依赖包袱** —— 只用系统自带的 .NET Framework 与 WebView2 运行时；编译由 `build.ps1` 首次自动引导 Roslyn 编译器（下载到 `tools\roslyn\`，不进仓库），失败时回退 Windows 自带的 `csc.exe`；不需要 Visual Studio、不需要安装 SDK
- **应用图标** —— 罗盘环扁平图标（`icons/D-compass.svg`），构建时经 csc `/win32icon:` 直接嵌入 exe（无 `.resx`/`.rc` 资源文件）；ICO 含 16~256 共 9 档，小尺寸按档重绘以保证可辨识
- **单实例 + 真·多窗口** —— 重复启动会把地址转发给已开窗口（适合当默认浏览器）；右键可在进程内开新窗口，共享同一个内核环境，不争抢用户数据目录
- **新标签页 = 书签墙** —— 新标签页直接展示全部书签，每个书签带网站图标
- **启动页可配置** —— 浏览器启动时显示空白页（默认）/ 书签墙 / 你指定的任意网站；「＋新标签」不受影响，永远显示书签墙
- **本地文件直开** —— 双击 `.html` 文件（设为默认打开方式后），或把文件拖进窗口

---

## 功能

### 浏览

- **地址栏**：自动识别四类输入 —— 完整网址（原样打开）、本地路径（盘符 / UNC / 已存在的文件 → 规范化为 `file:///`）、带点号的域名（补 `https://`）、其余当关键词交给 Bing 搜索；右侧「复制」「粘贴」按钮可把地址栏内容送入剪贴板 / 从剪贴板回填地址栏
- 前进 / 后退 / 刷新；多标签页
- **标签页外观**：标题字号加大（10.5pt）、标签之间留出明显间隙、**所有标签宽度统一**（210px 固定尺寸，超出用省略号，悬停看完整标题）
- **贴边分屏（Aero Snap）**：把窗口拖到屏幕左/右边缘松手即为左/右半屏，拖到顶部即最大化，从最大化拖离立刻还原成原来的大小；双击标题栏最大化/还原。缩放与吸附都在不引入 `WS_THICKFRAME` 的前提下实现，因此**玻璃在失焦时依然常驻**（见 ADR-0010）
- **链接分流**：同站跳转留在当前标签（历史可回溯），跨站链接才新开标签页
- **右键菜单**：后退 / 前进 / 刷新、「在新标签页中打开链接」、「在新窗口中打开链接」、「复制链接地址」、「复制图片地址」、「在新标签页中打开图片」；选中文字时可一键「在新标签页中搜索」（交给 Bing）；当前页也能「在新标签页中打开」「复制当前页面地址」；输入框场景保留系统默认的复制/粘贴菜单
- **网页缩放（50% ~ 200%）**：「查看」菜单（放大 / 缩小 / 实际大小 + 11 档预设）与工具栏的缩放下拉**任何时候都可用**（50 / 67 / 75 / 80 / 90 / 100 / 110 / 125 / 150 / 175 / 200%）；焦点在地址栏等本程序自己的控件上时，`Ctrl + +` 放大、`Ctrl + -` 缩小、`Ctrl + 0` 恢复 100%（含小键盘与 `Ctrl + Shift + =` 写法）以及 `Ctrl + 滚轮` 同样生效 —— 只用进程内消息过滤器，只拦发给本窗口的消息，不碰程序之外的任何输入。比例按标签保存、换页不丢；调过之后新开的标签页与弹出小窗都沿用，到两端自动封顶 / 触底并置灰对应菜单项（会话内有效，不落盘）
- **两套全屏，各管各的**（见 ADR-0011）：
  - **视频真全屏**：网页调用标准 Fullscreen API（B 站播放器右下角「全屏」按钮、`<video>.requestFullscreen()`）时自动进入 —— WebView2 脱离标签页直接铺满**整块屏幕（含任务栏）**，标题栏 / 菜单 / 工具栏 / 标签栏全部隐藏。真全屏时窗口的 6px 缩放热区（`Padding`）一并归零，因此视频四周**没有那圈玻璃边框**；退出时逐项还原
  - **网页全屏**：`F11` 或「查看 → 网页全屏」触发。只让**外壳退场** —— 菜单 / 工具栏 / 地址栏 / 标签头隐藏、网页铺满客户区（网页一并抬到窗口上，否则 `TabControl` 的 4px 边框会留边），但**标题栏保留**（还能拖动、最大化、关闭），窗口只最大化到**工作区**（**任务栏可见、不置顶**）
  - 两者是独立状态且**可叠加**：网页全屏中进视频全屏，退出视频全屏后仍是网页全屏。焦点在网页里时宿主收不到按键，`F11` 由注入页面的脚本 `postMessage` 转发回宿主（与新标签页点击桥同一机制，**不用系统级钩子**）
- **弹出小窗（含置顶）**：站点用 `window.open` 弹出的小窗（B 站画中画、OAuth 授权窗等）由应用**自建窗口**承载 —— 默认**窗口置顶**（切到其他应用时仍显示在最前），窗口顶部带「窗口置顶」开关可随时开关；主窗口「工具 → 小窗默认置顶」可改默认值并会记住；窗口尺寸按站点请求设置，页面调用 `window.close()` 时窗口自动关闭

### 历史记录

- 菜单「历史 → 查看历史记录...」：按天分组（今天 / 昨天 / 日期）、**新→旧**排列，列出每条的时间、标题、网址
- **逐条删除**（支持多选批量删）与**一键清空全部**（会一并清掉内核侧的历史库，避免两处不一致）
- 双击某条即可在新标签页打开
- 相邻同址的重复访问（刷新、重定向）自动合并为一条并刷新时间；内部页（新标签页、`about:blank`）不记录
- 数据落在 `%APPDATA%\LoongBrowser\history.json`，默认最多保留 3000 条（超出丢最旧的）

### 新标签页（书签墙）

- 「＋新标签」显示所有书签：32px 图标 + 标题（两行截断，悬停显示完整标题与网址）
- **图标三级来源**：① WebView2 官方 `FaviconChanged` + `GetFaviconAsync`（站点自查声明的图标）→ ② 兜底抓 `https://<host>/favicon.ico` → ③ 内置空白图标
- 所有图标统一归一化为 **32×32 PNG** 缓存到本地，离线也能显示；拿不到图标的站点不会反复重试
- 空书签时显示引导页（浅色主题；暗色模式已按 ADR-0002 移除）
- 工具菜单可一键关闭（回到空白新标签页）

### 启动页

「工具 → 启动页设置...」三选一，立即保存、下次启动生效：

| 选项 | 启动时 |
|---|---|
| 空白页（默认） | `about:blank`，与加此功能之前完全一致 |
| 书签墙 | 启动即展示全部书签（页头与标签都写「启动页」） |
| 打开指定网站 | 你填的那个地址；也可一键填入"当前标签页地址" |

- **与「＋新标签」的分工**：那个按钮**永远**显示书签墙，不受这里的设置影响
- 填地址时同样支持补协议、本地路径（转 `file:///`）与关键词搜索；`javascript:` / `data:` / `about:` 这类伪协议会被拒绝
- 命令行带了地址（双击 `.html`、设为默认浏览器后从外部打开）时**优先于**启动页设置 —— 否则双击文件会被启动页抢走
- 设置落在 `%APPDATA%\LoongBrowser\startup.json`；删掉该文件即恢复默认（空白页）

### 数据与设置

- **书签**：一键收藏本页；管理器支持打开 / 删除
- **下载**：自动保存到系统「下载」文件夹并记录；支持打开文件 / 文件夹、删除记录
- **清理**：缓存 / 历史记录 / Cookie 分项清理（调用 WebView2 官方 API）
- **设为默认浏览器**：自动完成 HKCU 登记并引导到系统设置完成确认。登记只有一个实现（`FileAssociations`），安装器与主程序共用；每次登记都指向「当前正在运行的这份 exe」，并顺手清掉指向已删目录的历史条目 —— 「打开方式」里始终只有一条 LoongBrowser

### 打开本地文件

- 双击 `.html` / `.htm`：需要在「打开方式」里选一次 LoongBrowser 并勾选「始终使用」（Windows 10/11 不允许程序自行抢占默认程序，这一步只能手动确认一次）
- 把文件拖进窗口：直接打开

---

## 下载与安装

从 [Releases](https://github.com/longge1213812138/LoongBrowser/releases) 下载：

| 文件 | 说明 |
|---|---|
| `LoongBrowser-v1.2.0-win64.zip` | **推荐**：完整安装包（安装器 + 主程序 + 3 个依赖 DLL）。解压后双击 `LoongBrowserSetup.exe` |
| `LoongBrowserSetup.exe` | 安装器单文件。⚠️ 它从**自身所在目录**复制文件，必须与 `LoongBrowser.exe` 及 3 个依赖 DLL 放在同一目录才能工作 |

安装：选择安装位置（默认 `%LocalAppData%\Programs\LoongBrowser`，可自定义）→ 点「安装」→ 可选引导设置默认浏览器。
卸载：安装器点「卸载」，或在 Windows「设置 → 应用」中卸载，无残留。

> 首次运行若提示缺少 WebView2 运行时，按提示安装微软官方组件（约 2MB 引导器）即可；Windows 10/11 多数已自带。

---

## 数据与目录

| 路径 | 内容 |
|---|---|
| `%APPDATA%\LoongBrowser\bookmarks.json` | 书签 |
| `%APPDATA%\LoongBrowser\history.json` | 浏览历史（最多 3000 条，**DPAPI 加密存储**，只有当前用户能解开） |
| `%APPDATA%\LoongBrowser\downloads.json` | 下载记录 |
| `%APPDATA%\LoongBrowser\favicons\` | 网站图标缓存（一个域名一个 PNG，删掉即自动重建） |
| `%APPDATA%\LoongBrowser\popup.json` | 小窗设置（是否默认置顶） |
| `%APPDATA%\LoongBrowser\startup.json` | 启动页设置（空白页 / 书签墙 / 指定网站） |
| `%APPDATA%\LoongBrowser\kernel.log` | 内核初始化诊断日志（**仅失败时写入**；SDK/运行时版本、数据目录状态、完整异常链。设环境变量 `LOONGBROWSER_KDIAG=1` 可记录正常启动） |
| `%LOCALAPPDATA%\LoongBrowser\WebView2\` | 内核数据（缓存 / Cookie / 会话） |

---

## 从源码构建

无需安装任何开发环境（Visual Studio / SDK 都不需要）：

1. 把 WebView2 官方 SDK 的 3 个 DLL 放进 `libs\`（仓库不含二进制，需自行获取一次）：
   `Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`
   —— 取自 NuGet 包 [`Microsoft.Web.WebView2`](https://www.nuget.org/packages/Microsoft.Web.WebView2)：
   解压 `.nupkg` 后，前两个在 `lib\net45\`，`WebView2Loader.dll` 在 `runtimes\win-x64\native\`（或 `win-x86`）。
2. 编译：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物在 `dist\`（主程序 + 安装器 + 3 个依赖 DLL），日志见 `build_log.txt`。

**编译器说明**：`build.ps1` 首次运行时自动从 NuGet 拉取 Roslyn 编译器（`microsoft.net.compilers.toolset` 4.8.0）到 `tools\roslyn\` 并以此编译（支持现代 C# 语法，如字符串插值）。**离线环境**会回退到 Windows 自带的 legacy `csc.exe`（仅支持 C# 5）——若代码已使用插值语法，离线构建会失败；联网跑一次即可补齐 Roslyn。

> `libs\`、`dist\`、`build_log.txt`、`temp\`、`tests\_out*\` 属于构建输入/产物，已在 `.gitignore` 中排除。

---

## 测试

全部测试不依赖任何测试框架：独立 `csc` 编译成小 exe 直接跑，部分用例跑在**真实 WebView2 内核**上（隐藏窗口 + 独立用户数据目录）。

| 套件 | 命令 | 覆盖 | 用例数 |
|---|---|---|---|
| URL 归一化 | `powershell -File tests\run_normalizeurl_test.ps1` | 本地路径 / UNC / 含空格与中文路径、协议补全、搜索回落、过滤 `javascript:` | 25 |
| 同站判定 | `powershell -File tests\run_samesite_test.ps1` | 链接分流规则（同站 / 跨站 / 仿冒域名） | 10 |
| 弹窗分流 | `powershell -File tests\run_popup_route_test.ps1` | `target="_blank"` / `window.open` 的分流：同站留标签、跨站开标签、程序化小窗交回内核 | 27 |
| 新标签页（单元） | `powershell -File tests\run_newtab_test.ps1` | 页面生成、HTML 转义、危险协议过滤、图标内联、数量上限 | 42 |
| 打开本地 HTML（端到端） | `powershell -File tests\run_e2e_openfile_test.ps1` | 真实内核：修复前的 DNS 失败 vs 修复后正常渲染 | 5 |
| 新标签页（端到端） | `powershell -File tests\run_newtab_e2e.ps1` | 真实内核 + 真实 `TabManager`：渲染、图标两条通道、点击桥与越权拒绝 | 18 |
| 启动页设置（单元） | `powershell -File tests\run_startup_test.ps1` | 三种启动模式的决策与优先级、伪协议拦截、盘符与 host:port 不误判、落盘往返与损坏回落、墙标题与标签页标题分离、对话框三选一与校验 | 85 |
| 启动页（端到端） | `powershell -File tests\run_startup_e2e.ps1` | 真实内核 + 真实 `MainForm`：三种模式真的打开对应页面、设置跨重启保留；并逐项验证「＋新标签」在三种模式下都仍是书签墙 | 20 |
| 关闭后残留（回归） | `powershell -File tests\run_residue_test.ps1` | 真实 `LoongBrowserContext` + 真实 `MainForm`：悬停出提示窗后关窗、以及「启动页收藏→关窗」两条路径，`Application.Run` 必须返回；关窗前 `OpenForms` 里还挂着 `ChromeTooltip` | 11 |
| 打开方式条目唯一（回归） | `powershell -File tests\run_assoc_test.ps1` | 用 Explorer 自己的 `SHAssocEnumHandlers` 判定（与右键菜单同口径）：登记后只出现一条 LoongBrowser、指向当前 exe；塞入三类历史遗留后确实变成多条，清理后回到一条；其它应用不受影响 | 8 |
| 画中画弹窗（端到端） | `powershell -File tests\run_pip_popup_e2e.ps1` | 真实内核：复现修复前「当前页被顶成空白页」、验证修复后页面不受影响且小窗被允许；并探测真实 B 站视频页 | 8 |
| 小窗置顶（端到端） | `powershell -File tests\run_popup_window_e2e.ps1` | 真实内核 + 真实 `TabManager`：小窗创建、默认置顶、开关切换与记忆、按请求尺寸、opener 可写入小窗、`window.close()` 联动关闭 | 12 |
| 浏览历史（单元） | `powershell -File tests\run_history_test.ps1` | 记录与排序、只记可导航地址、同址去重、标题回填、删除/清空、条数上限、落盘与重载、时间分组 | 27 |
| 浏览历史（端到端） | `powershell -File tests\run_history_e2e.ps1` | 真实内核 + 真实 `TabManager`：访问真实站点写入历史并带标题、内部页不记、新→旧、刷新去重、删除与清空落盘 | 13 |
| 标签页外观 | `powershell -File tests\run_tabstrip_test.ps1` | 字号/固定尺寸/单行/悬停提示、每个标签宽高一致、相邻不重叠；并输出 `tests\ui_tabs_preview.png` 预览图供人工核对观感 | 12 |
| 缩放逻辑（单元） | `powershell -File tests\run_zoom_test.ps1` | 50%~200% 区间约束、1% 归一化、步进与两端封死、预设档位、`Ctrl+=/-/0` 与 `Ctrl+滚轮` 的判定（不误伤 `Ctrl+C`/`Ctrl+Shift+D`） | 74 |
| 网页缩放（端到端） | `powershell -File tests\run_zoom_e2e.ps1` | 真实内核 + 真实 `TabManager`：比例真的写进内核、两端封顶、新标签页继承会话比例、换页不丢、按屏幕坐标命中标签且不影响隐藏标签 | 29 |
| F11 轻量全屏（端到端） | `powershell -File tests\run_fullscreen_e2e.ps1` | 真实窗口结构 + 真实内核：进出后菜单/工具栏/窗口状态原样还原、标签栏与标题栏保留、页面内 F11 转发链（注入脚本 → postMessage → 切换）、与视频真全屏共存不串状态、退出视频后网页仍挂在窗口上直到网页全屏也退出 | 39 |
| 两套全屏（真实 MainForm） | `powershell -File tests\run_fullscreen_test.ps1` | 真实 `MainForm` + 真实 WebView2：视频真全屏时 `Padding` 归零、WebView2 铺满整块屏幕、标题栏/外壳/标签栏全隐、覆盖 `Screen.Bounds`、置顶；网页全屏时外壳隐但标题栏留、只最大化到工作区（**不盖任务栏**）、不置顶、网页在标题栏之下铺满客户区；两套叠加（引用计数不互相踩）与逐项还原 | 47 |
| 玻璃常驻 + 缩放能力（回归） | `powershell -File tests\run_glass_persist_test.ps1` | 窗口样式**不含** `WS_THICKFRAME`（含它则失焦必丢玻璃）、边缘八向命中、边缘映射到 `HT*`、贴边分屏判定 | 17 |
| 贴边分屏（单元） | `powershell -File tests\run_snaptest.ps1` | 贴边阈值判定、左右半屏矩形正好拼满工作区（含奇数宽度）、贴顶最大化、从最大化拖离的还原几何 | 19 |
| 缩放/分屏 + 玻璃（端到端） | `powershell -File tests\run_snap_test.ps1` | 真实窗口：样式不含 `WS_THICKFRAME`、最大化铺满工作区不盖任务栏、还原回原位、吸附矩形与玻璃状态 | 12 |
| 工具栏悬停提示（回归） | `powershell -File tests\run_tooltip_test.ps1` | 悬停 1 秒才弹、文字 = 按钮名字、换按钮立刻收起并重新计时、移出/点击收起、提示挂在按钮上方不压标签条 | 17 |
| 玻璃外壳（回归） | `powershell -File tests\run_glass_test.ps1` | 外壳/标题栏每帧先清面再画前景（窗口变窄不留旧地址栏）、绘制前布局定稿、`GlassManager.Apply` 幂等、玻璃必须显示后再应用（不在构造函数里）、清面后整块重画不被失效矩形裁剪 | 9 |
| 外壳渲染（截图） | `powershell -File tests\run_chrome_preview.ps1` | 字体/字号/纯黑文字、布局与命中矩形、玻璃区每帧清面且不铺底色、前景文字与图标确实画出；输出 `tests\chrome_preview*.png` 供人工核对 | 24 |

运行结果写入 `tests\*_log.txt`。合计 **610** 项用例。

---

## 项目结构

```
build.ps1                一键编译：主程序 + 安装器 + 拷贝依赖到 dist\（首次自动 bootstrap Roslyn 到 tools\roslyn\；经 /win32icon: 嵌入应用图标）
icons/                   应用图标：SVG 源文件 + LoongBrowser.ico（9 档）+ _make_ico.py 再生成脚本
src/
  Program.cs             入口：单实例互斥、命令行 URL 解析、进程内多窗口管理
  MainForm.cs            主窗口：菜单 / 工具栏 / 地址栏 / 标签容器，URL 归一化
  WindowResizer.cs       边缘/角落命中与 HT* 映射（交给系统起缩放循环；刻意不用 WS_THICKFRAME，见 ADR-0010）
  SnapController.cs      Aero Snap：贴边左右半屏、贴顶最大化、从最大化拖离还原（自实现，见 ADR-0010）
  ChromeTooltip.cs       工具栏按钮的悬停提示窗（1 秒后显示名字；分层窗口色键透明，不抢焦点）
  FullscreenManager.cs   两套全屏：视频真全屏（Fullscreen API，Padding 归零、铺满整屏、置顶）+ 网页全屏（F11/菜单，外壳让位、标题栏与任务栏保留），页面内 F11 转发与多窗口状态隔离
  TabManager.cs          标签页与 WebView2 生命周期、右键菜单、图标采集、新标签页渲染、网页缩放 API、内核初始化失败自救
  KernelDiag.cs          内核初始化诊断：环境事实 + 异常链落盘（仅失败时写；LOONGBROWSER_KDIAG=1 记录正常路径）
  ZoomManager.cs         网页缩放：50%~200% 区间与步进、预设档位、按键/滚轮判定
  NewTabPage.cs          新标签页（书签墙）HTML 生成与安全过滤
  StartupPage.cs         启动页设置（空白页 / 书签墙 / 指定网站）+ 设置对话框
  FileAssociations.cs    「打开方式」登记的唯一出口：身份键、ProgId、清理失效的历史条目、Explorer 同口径枚举
  FaviconCache.cs        网站图标三级来源 + 归一化缓存
  BookmarkStore.cs       书签存取 + 书签管理对话框
  DownloadStore.cs       下载记录
  BrowserCleaner.cs      缓存 / 历史 / Cookie 清理
  DefaultBrowser.cs      默认浏览器与 .html 打开方式的注册表登记
  JsonStore.cs           JSON 读写 + 应用目录约定
  Installer.cs           自研安装器（独立编译）
tests/                   测试与 API 诊断工具（见上表），以及各套件的 runner 脚本
```

---

## 技术栈

| 组件 | 说明 |
|---|---|
| 内核 | Microsoft Edge WebView2（Chromium），使用系统自带运行时 |
| 框架 | C# WinForms（.NET Framework，系统自带） |
| 编译 | 首次构建自动 bootstrap Roslyn（`tools\roslyn\`，不进仓库，支持现代 C#）；离线回退 Windows 自带 legacy `csc.exe`（仅 C# 5），`/codepage:65001`，无需 Visual Studio |
| 打包 | 自研 C# 安装器，无第三方打包工具 |
| 依赖 | WebView2 官方 SDK 3 个 DLL（Core / WinForms / Loader），约 880 KB |

---

## 可调参数

都是源码中的静态字段，改一行即生效：

| 参数 | 默认值 | 说明 |
|---|---|---|
| `NewTabPage.Enabled` | `true` | 新标签页是否显示书签墙（工具菜单可切换） |
| `StartupPageSettings.Mode` | `blank` | 启动页：`blank` 空白页 / `wall` 书签墙 / `url` 指定网站（工具菜单可改，写入 `startup.json`） |
| `NewTabPage.UseLetterFallback` | `false` | 无图标时改用「首字母色块」而不是空白图标 |
| `NewTabPage.MaxCards` / `MaxIcons` | `500` / `60` | 书签卡片数 / 内联图标数上限 |
| `FaviconCache.EnableRemoteFetch` | `true` | 是否允许联网抓 `/favicon.ico`（关闭后只用已访问站点的图标 + 空白图标） |
| `FaviconCache.RemoteTimeoutMs` | `6000` | 图标抓取超时 |
| `FaviconCache.IconSize` | `32` | 图标边长（同时是缓存尺寸） |
| `PopupWindow.DefaultAlwaysOnTop` | `true` | 弹出小窗是否默认窗口置顶（工具菜单可改，会写入 `popup.json`） |
| `PopupWindow.BarHeight` | `30` | 小窗顶部工具条高度（置顶开关所在的那一条） |
| `HistoryStore.MaxItems` | `3000` | 历史最多保留多少条（超出丢最旧的） |
| `HistoryStore.DedupeSeconds` | `30` | 相邻同址访问的去重窗口（秒）；设为负数=每次访问都记 |
| `HistoryStore.FilePath` | `%APPDATA%\...\history.json` | 历史数据文件位置 |
| `TabStrip.TabWidth` / `TabHeight` | `210` / `40` | 标签统一尺寸 |
| `TabStrip.GapX` / `GapTop` | `9` / `6` | 标签之间的水平间隔 / 与顶部的距离 |
| `TabStrip.TitleSize` | `10.5` | 标签标题字号（pt） |
| `ZoomManager.MinZoom` / `MaxZoom` | `0.5` / `2.0` | 网页缩放的下限 / 上限（50% ~ 200%） |
| `ZoomManager.Step` | `0.1` | 放大、缩小与 Ctrl+滚轮每级的步进（10%） |
| `ZoomManager.Presets` | 11 档 | 菜单与工具栏可直接点选的预设比例 |

---

## 已知边界

- **不是通用浏览器**：无扩展、无账号同步、无密码管理、无内置 PDF 阅读器（PDF 交给系统默认程序）
- **图标**：SVG 图标不支持（`System.Drawing` 无法栅格化）→ 回落为空白图标；站点明确返回 404 时会被记成「无图标」，站点之后换了图标需要清空 `favicons\` 目录
- **隐私**：图标兜底抓取会向每个书签域名各发 1 次 `https://<host>/favicon.ico` 请求；不想暴露书签域名请关闭 `FaviconCache.EnableRemoteFetch`
- **大书签量**：书签墙一次渲染全部（上限 500 张卡片），上千条建议改为分页或虚拟滚动
- **默认程序**：Windows 10/11 不允许程序自行抢占默认浏览器，需要在系统设置中手动确认一次
- **历史记录是本应用自己记的**：WebView2 只提供「清除历史」接口（`ClearBrowsingDataAsync`），**没有读取历史的 API**，所以历史由应用记录在 `%APPDATA%\LoongBrowser\history.json`。因此它只包含在本应用里访问过的页面（不会同步系统里其它浏览器的历史），清空历史也会一并清掉内核侧的历史库。
- **标签页**：宽度统一为固定值，标题过长会省略号截断（悬停可看完整标题）；标签过多时单行滚动，不会折行
- **缩放记忆**：50%~200% 的缩放只保存在当前会话内存里（按标签生效，新开标签页与小窗沿用），重启后回到 100%；未做按站点记忆（不同网站分别记住比例）
- **网页有焦点时的键盘 / 滚轮缩放不接管**：网页成为焦点后，按键与滚轮消息的接收方是 WebView2 内核**进程**里的窗口（与宿主不同 pid），不会进入本程序的消息队列；要接管只能装系统级低级钩子（`WH_KEYBOARD_LL` / `WH_MOUSE_LL`，全局监听键鼠），权衡后明确不做 —— 不为一个快捷键引入系统级输入监听。那种情形请用「查看」菜单 / 工具栏下拉缩放；内核自身对部分缩放快捷键有响应（早期实测 `Ctrl + =` 有效），但 `Ctrl + -` / `Ctrl + 0` / `Ctrl + 滚轮` 不作保证
- **F11 在网页焦点下的入口靠页面内转发**：注入脚本监听 `keydown` 后 `postMessage` 回宿主；若某页面删改了 `chrome.webview`（极少见）导致转发失效，点一下地址栏（焦点回到宿主）再按 `F11` 即可，全屏状态本身不受影响
- **内核初始化失败有自救与诊断**：`EnsureCoreWebView2Async` 失败（如 `0x800700AA` 资源被占用）时，程序会丢弃可能损坏的内核环境、清理数据目录里的陈旧锁文件并自动重试一次；仍失败才弹窗，弹窗含错误码、SDK 与运行时版本、日志路径。完整细节落在 `%APPDATA%\LoongBrowser\kernel.log`
- **平台**：仅 Windows 10/11（依赖 WebView2 与 .NET Framework）

---

## 许可

仓库暂未附带 LICENSE 文件（默认保留所有权利）。如需开源，请自行添加许可文件。

---

## 相关文档

- [开发计划书.md](开发计划书.md) —— 需求与设计取舍
- [双击打开HTML报错-根因分析与修复报告.md](双击打开HTML报错-根因分析与修复报告.md) —— v1.0.1：双击本地 HTML 报 DNS 错的根因分析与修复
- [新标签页书签墙-实现说明与边界.md](新标签页书签墙-实现说明与边界.md) —— 书签墙的实现方式、配置项与边界清单
- [docs/adr/0008-startup-page-vs-new-tab.md](docs/adr/0008-startup-page-vs-new-tab.md) —— 为什么启动页与「＋新标签」必须分开，以及默认值为何保持空白页
- [docs/adr/0009-exit-criteria-and-single-association.md](docs/adr/0009-exit-criteria-and-single-association.md) —— 退出判据为何只看浏览器窗口；「打开方式」为何必须只有一个实现
- [docs/adr/0010-self-implemented-aero-snap.md](docs/adr/0010-self-implemented-aero-snap.md) —— 为什么 Aero Snap 必须自己做；玻璃常驻与分屏如何兼得
