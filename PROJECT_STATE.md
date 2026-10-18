# PROJECT_STATE — LoongBrowser 交接材料

> 生成时间：2026-09-25 13:20 左右，更新：2026-09-25 13:30（Git 变更已处理）｜ 状态：**Git 变更已提交，待文档同步**
> 工作目录：`C:\Users\91533\Desktop\LoongBrowser-master` ｜ 分支：`main` ｜ HEAD：`cbe0fb1`

## 一、项目目标

LoongBrowser：极简 Windows 桌面浏览器（WebView2/Chromium 内核 + C# WinForms），零第三方运行时依赖，交付可安装 EXE。设计依据见《开发计划书.md》（v1.1）。

## 二、已提交成果（git main，至 cbe0fb1）

| 提交 | 内容 |
| --- | --- |
| `cbe0fb1` | 构建升级：build.ps1 首次自动 bootstrap vendored Roslyn（tools/roslyn/），legacy csc 回退；.gitignore 忽略 tools/roslyn/ 与 .codeindex/ |
| `042d8ba` | 网页翻译功能（百度翻译 API，src/TranslationService.cs、TranslationDialog.cs） |
| `b3813aa` | 地址栏复制/粘贴按钮 + 更丰富的右键菜单 |
| `f3dbc92` | 视频播放全屏支持（WebView2 Fullscreen API） |
| `6522626` | libs/ 纳入版本控制（WebView2 SDK 3 DLL，~890 KB），裸 clone 可构建 |
| `60cf2bf` | 初始提交（源码/测试/文档） |

功能全集与使用说明见 `README.md`（权威资料：功能描述以此为准）。

## 三、Roslyn 构建升级（已提交 cbe0fb1）

**改动目标**：把构建编译器从系统自带 legacy csc（仅 C# 5）升级为 vendored Roslyn，让代码可以使用字符串插值等现代 C# 语法，同时仍编译为 .NET Framework 4 目标、保持零运行时依赖。

- `build.ps1`：新增 `Ensure-Roslyn` bootstrap —— 首次构建时从 NuGet 拉 `microsoft.net.compilers.toolset` 4.8.0，解压 `tasks/net472/*` 到 `tools/roslyn/`；拉取失败则回退 legacy csc（并警告 C#5 不支持插值）。Roslyn 时追加 `/langversion:latest`。
- `.gitignore`：已补上 `tools/roslyn/` 与 `.codeindex/` 实际忽略模式（已提交 cbe0fb1）。

**验证证据**：2026-09-25 13:19 本机执行 `powershell -File build.ps1` → 输出 `Using Roslyn compiler: ...tools\roslyn\csc.exe` + `BUILD OK`；产物 LoongBrowser.exe 104 KB、LoongBrowserSetup.exe 15 KB、3 个 WebView2 DLL 复制正常（build_log.txt 仅 2 行，无详细日志）。

**尚未验证**：新 exe 未在实机跑过回归（打开网页/翻译/全屏/画中画等）；测试目录 tests/ 的 exe 未随本次构建重建（build.ps1 只构建主程序与安装器）。

## 四、决定与限制

- 已确认（用户约束，来自开发计划书）：单一语言、零第三方运行时依赖、无 VS/NuGet 常备工具链；编译产物不得引入新运行时依赖。
- 本会话 Agent 选择：Roslyn 走「首次构建自动 bootstrap + legacy 回退」路线，而非要求用户手工装工具链；roslyn 工具包选 net472 版（与 .NET Framework 4.x 目标匹配）。
- 已否决：CEF/完整 Chromium 打包（体积）、Visual Studio 依赖（违背最小工具链）。
- 注意：README 与开发计划书中「系统自带 csc、主程序 ~54 KB、代码按 C# 5 编写」等描述**已过时**（现用 Roslyn、exe 已 104 KB），文档同步未做。

## 五、下一步（给接手对话的第一步）

1. ~~处理未跟踪/忽略项~~ **已完成**（cbe0fb1）：用户拍板 tools/roslyn/ 与 .codeindex/ 均忽略；build.ps1 + .gitignore 已提交。
2. **同步文档（进行中）**：README「零依赖/编译工具链」段与开发计划书 §2.3、风险表中 csc/C#5 相关描述，改为 Roslyn bootstrap 方案（exe 体积 ~104 KB）。
3. **回归验证（可选）**：`dist\LoongBrowser.exe` 实机启动，冒烟网页/翻译/全屏/画中画小窗；必要时重建 tests 跑一遍。
4. 完成标准：`git status` 干净（除 dist/、.codeindex/、build_log.txt 等忽略项）+ 文档同步完成 + build.ps1 在裸 clone 下可复现 BUILD OK（首次需网络拉 Roslyn；离线时回退 legacy csc 可用但禁用插值语法——若代码已使用插值则离线构建会失败，这点要写进 README）。

## 六、简短交接区

- 本次交接编号：（源对话未提供会话标识，留空）
- 用户确认范围：整仓（LoongBrowser 全部）
- 交接状态：Git 变更已提交（cbe0fb1），文档同步进行中
- 接手开场白（可复制）：

```
继续 LoongBrowser。目录 C:\Users\91533\Desktop\LoongBrowser-master，
先读 PROJECT_STATE.md：Git 变更已全部提交（HEAD cbe0fb1），
接下来同步 README 与开发计划书的工具链描述（csc/C#5 → Roslyn bootstrap），
然后跑一次 build.ps1 验证 BUILD OK。
```

## 七、当前字体状态（2026-10-18 定稿）

外壳文字用**系统自带的微软雅黑**（`Microsoft YaHei UI` → `Microsoft YaHei` → `Segoe UI`），
纯黑 `#000000`，**不加阴影**。字号：`UIFont` 10pt、`TitleFont` 10.5pt、`TabStrip.TitleSize` 11.5pt。

曾试过两个方向，均已回退，理由见 `docs/adr/0006-yahei-black-text-no-shadow.md`：
- 黑体 `SimHei`：商业字体，跨版本渲染不一致，内嵌有授权红线。
- 内嵌思源黑体子集：授权合法且实测跑通，但把 exe 从 121 KB 撑到 3.1 MB，与极简定位冲突。

## 八、待办需求（用户提出，尚未实现）

- **字体缩放功能**（2026-10-18 提出）：用户将来要做界面字体缩放。当前字号硬编码在
  `VistaTheme.UIFont`（10pt）、`VistaTheme.TitleFont`（10.5pt）、`TabStrip.TitleSize`（11.5pt）三处，
  实现时需把这三处收敛到一个可缩放的入口（例如基于基准 pt 乘缩放系数），
  并注意自绘布局常量（`TitleBarHeight` 等）是否需要随之调整。
- **新标签页 HTML 的字体与外壳联动**：新标签页是 WebView2 页面，字体缩放若要覆盖网页侧，
  需在生成 CSS 时注入缩放后的 `font-size`。
