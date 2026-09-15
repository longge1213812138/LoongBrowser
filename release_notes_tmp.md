# LoongBrowser v1.0.1

极简 Chromium 内核浏览器（WebView2 + C# WinForms），零第三方运行时依赖。

## 下载与安装

| 文件 | 说明 |
|---|---|
| `LoongBrowser-v1.0.1-win64.zip` | **推荐**：完整安装包（安装器 + 主程序 + 3 个依赖 DLL）。解压后双击 `LoongBrowserSetup.exe` 即可 |
| `LoongBrowserSetup.exe` | 安装器单文件。⚠️ 它从**自身所在目录**复制文件，必须与 `LoongBrowser.exe` 及 3 个依赖 DLL 放在同一目录才能工作 |

安装位置默认 `%LocalAppData%\Programs\LoongBrowser`，可在安装界面自定义；卸载无残留。

## 本次修复

**双击 `.html` 文件报「找不到 c 的服务器 IP 地址 / ERR_NAME_NOT_RESOLVED」**

- **根因**：把本地文件路径当成了域名。双击文件时 Windows 按注册表命令 `"LoongBrowser.exe" "%1"` 把裸路径 `C:\...\x.html` 作为命令行参数传入，而 `NormalizeUrl` 因为字符串"含点号且无空格"把它拼成了 `https://C:\...\x.html`；URL 解析时盘符 `C` 落在主机名位置，内核就去 DNS 查询一台名叫 `c` 的服务器，自然失败。
- **为什么拖放正常**：拖入窗口的文件由 WebView2 内核自行转成 `file:///` URL，根本不经过这个函数。
- **修复**：`NormalizeUrl` 在"网址/搜索"启发式判断之前先识别本地路径（盘符 `C:\`/`C:/`、UNC `\\server\share`、以及确实存在的文件），转成正确转义（空格 / 中文 / `#`）的 `file:///` URL；顺带修掉"路径含空格会被当成搜索词"的隐患，并在命令行参数被空格拆散时重新拼回完整路径。
- 新增 `.html` / `.htm` 的"打开方式"登记（ProgId + OpenWithProgids）。

## 验证

- `NormalizeUrl` 单元测试 **25 / 25** 通过 —— `tests/normalizeurl_log.txt`
- 真实 WebView2 内核端到端 **5 / 5** 通过：旧逻辑 URL → `WebErrorStatus=HostNameNotResolved`（精确复现报错）；修复后 URL → 导航成功并渲染出页面标题 —— `tests/e2e_openfile_log.txt`
- Windows 自带 `csc.exe` 编译通过（BUILD OK）

完整根因分析与验证过程见仓库内《双击打开HTML报错-根因分析与修复报告.md》。

## 使用提示

安装后若双击 `.html` 仍由其他程序打开，请：右键任意 `.html` → 打开方式 → 选择其他应用 → LoongBrowser → 勾选「始终使用」。
（Windows 10/11 不允许程序自行抢占默认程序，这一步需要手动确认一次。）
