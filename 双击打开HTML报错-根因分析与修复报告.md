# 双击打开 HTML 报 ERR_NAME_NOT_RESOLVED —— 根因分析与修复报告

- 日期：2026-09-15
- 版本：LoongBrowser v1.0 → **v1.0.1**
- 结论：**不是 DNS、不是网络、不是防火墙问题，是"把本地文件路径当成域名拼成了 https:// 网址"。**

---

## 一、现象

| 打开方式 | 结果 |
|---|---|
| 双击 `.html` 文件 | 报错「找不到 **c** 的服务器 IP 地址，ERR_NAME_NOT_RESOLVED」 |
| 把文件拖进应用窗口 | 正常打开 |

同一个文件、同一个程序，差别只在"文件是**怎么**送进来的"。

---

## 二、两条路径的差异（问题关键）

| 环节 | 拖放打开（正常） | 双击打开（报错） |
|---|---|---|
| 谁接收文件 | **WebView2 内核自己**处理拖放（AllowExternalDrop，原生 C++ 实现） | **Windows 外壳**按注册表展开命令行 |
| 交给程序的形式 | 内核内部直接得到本地文件，转成 `file:///C:/.../x.html` | 命令行参数 `%1` = **裸路径** `C:\...\x.html` |
| 是否经过应用的 NormalizeUrl | **否**（内核直接导航） | **是**（Program.Main → NormalizeUrl） |
| 最终导航目标 | `file:///C:/Users/.../x.html` ✅ | `https://C:\Users\...\x.html` ❌ |

注册表实测（本机当前状态）：

```
HKCU\Software\Classes\Applications\LoongBrowser.exe\shell\open\command
    = "C:\Users\91533\Desktop\浏览器\dist\LoongBrowser.exe" "%1"
HKCU\Software\Classes\html_auto_file\shell\open\command
    = "D:\Program Files (x86)\LoongBrowser\LoongBrowser.exe" "%1"   ← 该 exe 已不存在
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.html\UserChoice
    ProgId = ChromeHTML
```

两种登记的命令行都是 `"程序" "%1"`，即**把文件的完整路径原样作为第一个参数**传进来。

---

## 三、根因

`src/MainForm.cs` 里负责"把用户输入变成可导航 URL"的函数 `NormalizeUrl`，判定顺序有缺陷：

```csharp
// 修复前
if (input.StartsWith("http://") || ... || input.StartsWith("file://") || input.StartsWith("localhost"))
    return input;                                    // ① 已有协议的放行
if (input.Contains(".") && !input.Contains(" "))
    return "https://" + input;                       // ② 含"."就当成域名 ← 致命
return "https://www.bing.com/search?q=" + ...;       // ③ 其余当搜索词
```

双击 `.html` 时，参数是 `C:\Users\91533\Desktop\test.html`：

1. 不以任何已知协议开头 → 不走 ①；
2. 含 `.`（`test.html`）且不含空格 → **命中 ②，被当成"域名"**；
3. 于是拼出 `https://C:\Users\91533\Desktop\test.html`。

接下来由 URL 解析规则决定结局：`://` 之后、第一个 `/` 或 `\` 之前的那一段是**主机名（authority）**，所以这一段变成了 `C:`。内核容错解析后把无效端口丢掉，**主机名就剩下 `c`（盘符 C）**，于是去 DNS 查询一台名叫 `c` 的服务器——当然查不到：

> ERR_NAME_NOT_RESOLVED =「找不到 **c** 的服务器 IP 地址」

**报错里的那个 `c`，就是盘符 `C`。这就是铁证。**

拖放之所以没事，是因为它根本不会走到 `NormalizeUrl`——WebView2 内核自己把文件转成了 `file:///` URL。

### 顺带发现的两个潜在缺陷

- 路径**含空格**时（如 `C:\My Docs\a.html`），会落入第 ③ 条 → 变成 Bing 搜索"我的文档在哪"。
- 如果注册表命令里的 `%1` 没写引号，含空格的路径会被 Windows 拆成多个命令行参数，`args[0]` 只剩前半截。

---

## 四、修复内容

| # | 文件 | 改动 |
|---|---|---|
| 1 | `src/MainForm.cs` | `NormalizeUrl`：在"网址/搜索"启发式**之前**先判定本地路径；新增 `IsLocalPath`（盘符 `C:\`、`C:/`、UNC `\\server\share`）与 `PathToFileUrl`（用 `Uri` 正确转义空格、中文、`#`） |
| 2 | `src/Program.cs` | 命令行参数若被空格拆散，用空格拼回完整路径，再交给 `NormalizeUrl` |
| 3 | `src/DefaultBrowser.cs` | 补上 `.html` / `.htm` 的 ProgId 与 OpenWithProgids 登记（只做"打开方式"候选登记，不抢占系统默认） |
| 4 | `src/Installer.cs`、`src/MainForm.cs` | 版本号 v1.0 → **v1.0.1**（便于确认装的是修复版） |

修复后的判定顺序：

```
已有协议(http/https/about/file/localhost) → 原样
本地路径(盘符 / UNC / File.Exists)        → file:///C:/...   ← 新增，双击走这条
含"."且不含空格                           → https:// + input
其余                                      → Bing 搜索
```

---

## 五、验证结果

### 1) 单元测试：25 / 25 通过
`tests/run_normalizeurl_test.ps1` → `tests/normalizeurl_log.txt`（直接调用真实的 `NormalizeUrl`，不是复制逻辑）

```
[PASS] A1 典型盘符路径    C:\Users\91533\Desktop\test.html → file:///C:/Users/91533/Desktop/test.html
[PASS] A4 路径含空格      C:\My Docs\my page.html         → file:///C:/My%20Docs/my%20page.html
[PASS] A6 中文路径        C:\资料\页面.html               → file:///C:/%E8%B5%84%E6%96%99/%E9%A1%B5%E9%9D%A2.html
[PASS] A9 带残留引号      "C:\Users\a.html"               → file:///C:/Users/a.html
[PASS] A10 UNC 网络路径   \\server\share\a.html           → file://server/share/a.html
[PASS] B1-B12 回归        http/https/域名/搜索/about/localhost 行为全部不变
[PASS] C1 机理复现        旧目标 https://C:\... 的"主机名"段 = <C:>，主机名即盘符 c
=== 通过 25 项，失败 0 项 ===
```

### 2) 端到端测试（真实 WebView2 内核）：5 / 5 通过
`tests/run_e2e_openfile_test.ps1` → `tests/e2e_openfile_log.txt`

```
内核版本: 152.0.4191.66
Step1 旧目标 https://C:\Users\...\e2e_double_click.html
      IsSuccess=False  WebErrorStatus=HostNameNotResolved     ← 精确复现你的报错
Step2 修复后 file:///C:/Users/91533/Desktop/%E6%B5%8F%E8%A7%88%E5%99%A8/...
      IsSuccess=True   WebErrorStatus=Unknown
      DocumentTitle=E2E_OPEN_OK                               ← 页面真的渲染出来了
=== 通过 5 项，失败 0 项 ===
```

### 3) 编译
`powershell -File build.ps1` → `build_log.txt`：**BUILD OK**，产物 `dist\LoongBrowser.exe`、`dist\LoongBrowserSetup.exe`。

---

## 六、你需要做的三步（让双击真正生效）

1. **退出所有正在运行的 LoongBrowser**（已经在跑的旧进程仍是旧代码；新进程只会把地址转发给它）。
2. **用新的程序**：重新运行 `dist\LoongBrowserSetup.exe` 安装，或直接用 `dist\LoongBrowser.exe`。
   > 注意：注册表里 `.html` 的默认值 `html_auto_file` 目前指向 `D:\Program Files (x86)\LoongBrowser\LoongBrowser.exe`，**这个文件已经不存在了**，所以双击很可能压根没走到修复版程序上。
3. **重新确认一次文件关联**：右键任意 `.html` → 打开方式 → 选择其他应用 → LoongBrowser → 勾选「始终」。
   （Windows 10/11 禁止程序自行抢占默认程序，这一步只能你手动点一次。）

**验证方法**：双击任意 `.html`，地址栏应显示 `file:///C:/...` 且页面正常显示；「关于」里应显示 **v1.0.1**。

---

## 七、本次改动文件清单

- 修改：`src/MainForm.cs`、`src/Program.cs`、`src/DefaultBrowser.cs`、`src/Installer.cs`
- 新增：`tests/NormalizeUrlTest.cs`、`tests/run_normalizeurl_test.ps1`、`tests/OpenFileE2ETest.cs`、`tests/run_e2e_openfile_test.ps1`
