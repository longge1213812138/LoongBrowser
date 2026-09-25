# LoongBrowser

极简 Windows 桌面浏览器：**WebView2（Chromium 内核）+ C# WinForms**，单语言实现，零第三方运行时依赖，编译无需安装 Visual Studio / SDK。

```
主程序 ~104 KB · 依赖仅 WebView2 官方 SDK 3 个 DLL（约 880 KB） · 首次构建自动下载 Roslyn 编译器（约 100 KB），无需手工安装工具链
```

---

## 亮点

- **零依赖包袱** —— 只用系统自带的 .NET Framework 与 WebView2 运行时；编译由 `build.ps1` 首次自动引导 Roslyn 编译器（下载到 `tools\roslyn\`，不进仓库），失败时回退 Windows 自带的 `csc.exe`；不需要 Visual Studio、不需要安装 SDK
- **单实例 + 真·多窗口** —— 重复启动会把地址转发给已开窗口（适合当默认浏览器）；右键可在进程内开新窗口，共享同一个内核环境，不争抢用户数据目录
- **新标签页 = 书签墙** —— 新标签页直接展示全部书签，每个书签带网站图标
- **本地文件直开** —— 双击 `.html` 文件（设为默认打开方式后），或把文件拖进窗口

---

## 功能

### 浏览

- **地址栏**：自动识别四类输入 —— 完整网址（原样打开）、本地路径（盘符 / UNC / 已存在的文件 → 规范化为 `file:///`）、带点号的域名（补 `https://`）、其余当关键词交给 Bing 搜索；右侧「复制」「粘贴」按钮可把地址栏内容送入剪贴板 / 从剪贴板回填地址栏
- 前进 / 后退 / 刷新；多标签页
- **标签页外观**：标题字号加大（10.5pt）、标签之间留出明显间隙、**所有标签宽度统一**（210px 固定尺寸，超出用省略号，悬停看完整标题）
- **链接分流**：同站跳转留在当前标签（历史可回溯），跨站链接才新开标签页
- **右键菜单**：后退 / 前进 / 刷新、「在新标签页中打开链接」、「在新窗口中打开链接」、「复制链接地址」、「复制图片地址」、「在新标签页中打开图片」；选中文字时可一键「在新标签页中搜索」（交给 Bing）；当前页也能「在新标签页中打开」「复制当前页面地址」；输入框场景保留系统默认的复制/粘贴菜单
- **网页缩放（50% ~ 200%）**：「查看」菜单（放大 / 缩小 / 实际大小 + 11 档预设）与工具栏的缩放下拉**任何时候都可用**（50 / 67 / 75 / 80 / 90 / 100 / 110 / 125 / 150 / 175 / 200%）；焦点在地址栏等本程序自己的控件上时，`Ctrl + +` 放大、`Ctrl + -` 缩小、`Ctrl + 0` 恢复 100%（含小键盘与 `Ctrl + Shift + =` 写法）以及 `Ctrl + 滚轮` 同样生效 —— 只用进程内消息过滤器，只拦发给本窗口的消息，不碰程序之外的任何输入。比例按标签保存、换页不丢；调过之后新开的标签页与弹出小窗都沿用，到两端自动封顶 / 触底并置灰对应菜单项（会话内有效，不落盘）
- **视频全屏**：网页调用标准 Fullscreen API（如 B 站播放器的「全屏」按钮）时，浏览器自动进入真全屏 —— WebView2 脱离标签页直接铺满屏幕，同时隐藏菜单 / 工具栏 / 标签栏；退出时一切恢复原状。注：站点自身的「网页全屏」（纯 CSS 变换）不属于 Fullscreen API，不在处理范围内
- **F11 网页全屏（轻量）**：按 `F11` 隐藏**菜单与工具栏**、窗口最大化；保留标题栏、标签栏与任务栏，再按一次 `F11` 退出，可见性与窗口状态原样还原。焦点在网页里时宿主收不到按键，改由注入页面的脚本 `postMessage` 转发回宿主（与新标签页点击桥同一机制，**不用系统级钩子**）；与上面的视频全屏是两套独立状态、可叠加：轻量全屏中进视频全屏，退出视频全屏后仍保持轻量全屏
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
- 空书签时显示引导页；跟随系统深浅色主题
- 工具菜单可一键关闭（回到空白新标签页）

### 数据与设置

- **书签**：一键收藏本页；管理器支持打开 / 删除
- **下载**：自动保存到系统「下载」文件夹并记录；支持打开文件 / 文件夹、删除记录
- **清理**：缓存 / 历史记录 / Cookie 分项清理（调用 WebView2 官方 API）
- **设为默认浏览器**：自动完成 HKCU 登记并引导到系统设置完成确认

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
| 画中画弹窗（端到端） | `powershell -File tests\run_pip_popup_e2e.ps1` | 真实内核：复现修复前「当前页被顶成空白页」、验证修复后页面不受影响且小窗被允许；并探测真实 B 站视频页 | 8 |
| 小窗置顶（端到端） | `powershell -File tests\run_popup_window_e2e.ps1` | 真实内核 + 真实 `TabManager`：小窗创建、默认置顶、开关切换与记忆、按请求尺寸、opener 可写入小窗、`window.close()` 联动关闭 | 12 |
| 浏览历史（单元） | `powershell -File tests\run_history_test.ps1` | 记录与排序、只记可导航地址、同址去重、标题回填、删除/清空、条数上限、落盘与重载、时间分组 | 27 |
| 浏览历史（端到端） | `powershell -File tests\run_history_e2e.ps1` | 真实内核 + 真实 `TabManager`：访问真实站点写入历史并带标题、内部页不记、新→旧、刷新去重、删除与清空落盘 | 13 |
| 标签页外观 | `powershell -File tests\run_tabstrip_test.ps1` | 字号/固定尺寸/单行/悬停提示、每个标签宽高一致、相邻不重叠；并输出 `tests\ui_tabs_preview.png` 预览图供人工核对观感 | 12 |
| 缩放逻辑（单元） | `powershell -File tests\run_zoom_test.ps1` | 50%~200% 区间约束、1% 归一化、步进与两端封死、预设档位、`Ctrl+=/-/0` 与 `Ctrl+滚轮` 的判定（不误伤 `Ctrl+C`/`Ctrl+Shift+D`） | 74 |
| 网页缩放（端到端） | `powershell -File tests\run_zoom_e2e.ps1` | 真实内核 + 真实 `TabManager`：比例真的写进内核、两端封顶、新标签页继承会话比例、换页不丢、按屏幕坐标命中标签且不影响隐藏标签 | 29 |
| F11 轻量全屏（端到端） | `powershell -File tests\run_fullscreen_e2e.ps1` | 真实窗口结构 + 真实内核：进出后菜单/工具栏/窗口状态原样还原、标签栏与标题栏保留、页面内 F11 转发链（注入脚本 → postMessage → 切换）、与视频真全屏共存不串状态 | 38 |

运行结果写入 `tests\*_log.txt`。合计 **340** 项用例。

---

## 项目结构

```
build.ps1                一键编译：主程序 + 安装器 + 拷贝依赖到 dist\（首次自动 bootstrap Roslyn 到 tools\roslyn\）
src/
  Program.cs             入口：单实例互斥、命令行 URL 解析、进程内多窗口管理
  MainForm.cs            主窗口：菜单 / 工具栏 / 地址栏 / 标签容器，URL 归一化
  FullscreenManager.cs   全屏管理：视频真全屏（Fullscreen API，含标签栏一起隐藏）+ F11 轻量全屏（隐藏菜单/工具栏、最大化），页面内 F11 转发与多窗口状态隔离
  TabManager.cs          标签页与 WebView2 生命周期、右键菜单、图标采集、新标签页渲染、网页缩放 API
  ZoomManager.cs         网页缩放：50%~200% 区间与步进、预设档位、按键/滚轮判定
  NewTabPage.cs          新标签页（书签墙）HTML 生成与安全过滤
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
| `NewTabPage.UseForStartup` | `false` | 启动窗口是否也显示书签墙（默认保持空白主页） |
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
- **平台**：仅 Windows 10/11（依赖 WebView2 与 .NET Framework）

---

## 许可

仓库暂未附带 LICENSE 文件（默认保留所有权利）。如需开源，请自行添加许可文件。

---

## 相关文档

- [开发计划书.md](开发计划书.md) —— 需求与设计取舍
- [双击打开HTML报错-根因分析与修复报告.md](双击打开HTML报错-根因分析与修复报告.md) —— v1.0.1：双击本地 HTML 报 DNS 错的根因分析与修复
- [新标签页书签墙-实现说明与边界.md](新标签页书签墙-实现说明与边界.md) —— 书签墙的实现方式、配置项与边界清单
