# LoongBrowser

极简 Windows 桌面浏览器：**WebView2（Chromium 内核）+ C# WinForms**，单语言实现，零第三方运行时依赖，编译只需要 Windows 自带工具链。

```
主程序 ~54 KB · 依赖仅 WebView2 官方 SDK 3 个 DLL（约 880 KB） · 无需 Visual Studio / NuGet / 打包工具
```

---

## 亮点

- **零依赖包袱** —— 只用系统自带的 .NET Framework 与 WebView2 运行时；编译调用 Windows 自带的 `csc.exe`，不需要 Visual Studio、不需要安装 SDK
- **单实例 + 真·多窗口** —— 重复启动会把地址转发给已开窗口（适合当默认浏览器）；右键可在进程内开新窗口，共享同一个内核环境，不争抢用户数据目录
- **新标签页 = 书签墙** —— 新标签页直接展示全部书签，每个书签带网站图标
- **本地文件直开** —— 双击 `.html` 文件（设为默认打开方式后），或把文件拖进窗口

---

## 功能

### 浏览

- **地址栏**：自动识别四类输入 —— 完整网址（原样打开）、本地路径（盘符 / UNC / 已存在的文件 → 规范化为 `file:///`）、带点号的域名（补 `https://`）、其余当关键词交给 Bing 搜索
- 前进 / 后退 / 刷新；多标签页
- **链接分流**：同站跳转留在当前标签（历史可回溯），跨站链接才新开标签页
- **右键菜单**：后退 / 前进 / 刷新、「在新标签页中打开链接」、「在新窗口中打开链接」、「复制链接地址」、「在新标签页中打开图片」；输入框场景保留系统默认的复制/粘贴菜单

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
| `LoongBrowser-v1.1.0-win64.zip` | **推荐**：完整安装包（安装器 + 主程序 + 3 个依赖 DLL）。解压后双击 `LoongBrowserSetup.exe` |
| `LoongBrowserSetup.exe` | 安装器单文件。⚠️ 它从**自身所在目录**复制文件，必须与 `LoongBrowser.exe` 及 3 个依赖 DLL 放在同一目录才能工作 |

安装：选择安装位置（默认 `%LocalAppData%\Programs\LoongBrowser`，可自定义）→ 点「安装」→ 可选引导设置默认浏览器。
卸载：安装器点「卸载」，或在 Windows「设置 → 应用」中卸载，无残留。

> 首次运行若提示缺少 WebView2 运行时，按提示安装微软官方组件（约 2MB 引导器）即可；Windows 10/11 多数已自带。

---

## 数据与目录

| 路径 | 内容 |
|---|---|
| `%APPDATA%\LoongBrowser\bookmarks.json` | 书签 |
| `%APPDATA%\LoongBrowser\downloads.json` | 下载记录 |
| `%APPDATA%\LoongBrowser\favicons\` | 网站图标缓存（一个域名一个 PNG，删掉即自动重建） |
| `%LOCALAPPDATA%\LoongBrowser\WebView2\` | 内核数据（缓存 / Cookie / 会话） |

---

## 从源码构建

只依赖 Windows 自带工具链，无需安装任何开发环境：

1. 把 WebView2 官方 SDK 的 3 个 DLL 放进 `libs\`（仓库不含二进制，需自行获取一次）：
   `Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`
   —— 取自 NuGet 包 [`Microsoft.Web.WebView2`](https://www.nuget.org/packages/Microsoft.Web.WebView2)：
   解压 `.nupkg` 后，前两个在 `lib\net45\`，`WebView2Loader.dll` 在 `runtimes\win-x64\native\`（或 `win-x86`）。
2. 编译：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物在 `dist\`（主程序 + 安装器 + 3 个依赖 DLL），日志见 `build_log.txt`。

> `libs\`、`dist\`、`build_log.txt`、`temp\`、`tests\_out*\` 属于构建输入/产物，已在 `.gitignore` 中排除。

---

## 测试

全部测试不依赖任何测试框架：独立 `csc` 编译成小 exe 直接跑，部分用例跑在**真实 WebView2 内核**上（隐藏窗口 + 独立用户数据目录）。

| 套件 | 命令 | 覆盖 | 用例数 |
|---|---|---|---|
| URL 归一化 | `powershell -File tests\run_normalizeurl_test.ps1` | 本地路径 / UNC / 含空格与中文路径、协议补全、搜索回落、过滤 `javascript:` | 25 |
| 同站判定 | `powershell -File tests\run_samesite_test.ps1` | 链接分流规则（同站 / 跨站 / 仿冒域名） | 10 |
| 新标签页（单元） | `powershell -File tests\run_newtab_test.ps1` | 页面生成、HTML 转义、危险协议过滤、图标内联、数量上限 | 42 |
| 打开本地 HTML（端到端） | `powershell -File tests\run_e2e_openfile_test.ps1` | 真实内核：修复前的 DNS 失败 vs 修复后正常渲染 | 5 |
| 新标签页（端到端） | `powershell -File tests\run_newtab_e2e.ps1` | 真实内核 + 真实 `TabManager`：渲染、图标两条通道、点击桥与越权拒绝 | 18 |

运行结果写入 `tests\*_log.txt`。

---

## 项目结构

```
build.ps1                一键编译：主程序 + 安装器 + 拷贝依赖到 dist\
src/
  Program.cs             入口：单实例互斥、命令行 URL 解析、进程内多窗口管理
  MainForm.cs            主窗口：菜单 / 工具栏 / 地址栏 / 标签容器，URL 归一化
  TabManager.cs          标签页与 WebView2 生命周期、右键菜单、图标采集、新标签页渲染
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
| 编译 | Windows 自带 `csc.exe`，`/codepage:65001`，无需 Visual Studio |
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

---

## 已知边界

- **不是通用浏览器**：无扩展、无账号同步、无密码管理、无内置 PDF 阅读器（PDF 交给系统默认程序）
- **图标**：SVG 图标不支持（`System.Drawing` 无法栅格化）→ 回落为空白图标；站点明确返回 404 时会被记成「无图标」，站点之后换了图标需要清空 `favicons\` 目录
- **隐私**：图标兜底抓取会向每个书签域名各发 1 次 `https://<host>/favicon.ico` 请求；不想暴露书签域名请关闭 `FaviconCache.EnableRemoteFetch`
- **大书签量**：书签墙一次渲染全部（上限 500 张卡片），上千条建议改为分页或虚拟滚动
- **默认程序**：Windows 10/11 不允许程序自行抢占默认浏览器，需要在系统设置中手动确认一次
- **平台**：仅 Windows 10/11（依赖 WebView2 与 .NET Framework）

---

## 许可

仓库暂未附带 LICENSE 文件（默认保留所有权利）。如需开源，请自行添加许可文件。

---

## 相关文档

- [开发计划书.md](开发计划书.md) —— 需求与设计取舍
- [双击打开HTML报错-根因分析与修复报告.md](双击打开HTML报错-根因分析与修复报告.md) —— v1.0.1：双击本地 HTML 报 DNS 错的根因分析与修复
- [新标签页书签墙-实现说明与边界.md](新标签页书签墙-实现说明与边界.md) —— 书签墙的实现方式、配置项与边界清单
