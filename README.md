# LoongBrowser 极简 Chromium 内核浏览器

基于 **WebView2（Chromium 内核）** 的极简 Windows 桌面浏览器。零第三方运行时依赖（.NET Framework 与 WebView2 运行时均为 Windows 10/11 自带），代码从简，单语言（C#）实现。

## 功能

- 🌐 **基础浏览**：地址栏（支持网址补全与关键词搜索）、前进/后退/刷新、多标签页；同站跳转留在当前标签（历史可回溯），跨站新开链接才新开标签页
- 📑 **单实例设计**：重复启动会把新网址转发给已打开的窗口，适合作为默认浏览器
- ⭐ **书签管理**：一键收藏本页；管理器支持打开/删除；持久化于 `%APPDATA%\LoongBrowser\bookmarks.json`
- ⬇️ **下载管理**：自动保存到系统"下载"文件夹并记录；支持打开文件/文件夹、删除记录
- 🧹 **缓存清理**：缓存 / 历史记录 / Cookie 分项清理（调用 WebView2 官方 API）
- 🖥️ **设为默认浏览器**：自动完成系统登记，并引导到 Windows 设置完成一键确认

## 安装

运行 `dist\LoongBrowserSetup.exe`：

1. 选择安装位置（默认 `%LocalAppData%\Programs\LoongBrowser`，可自定义）
2. 点击"安装"——自动复制文件、创建快捷方式、写入卸载信息
3. 可选：安装完成后引导设置默认浏览器

卸载：安装器点"卸载"，或 Windows"设置 → 应用"中卸载，全程无残留。

> 首次运行若提示缺少 WebView2 运行时，按提示下载微软官方组件（约 2MB 引导器）即可，大多数 Win10/11 已自带。

## 从源码构建

无需安装任何开发工具（使用 Windows 自带编译器）：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物在 `dist\`：`LoongBrowser.exe`（主程序）、`LoongBrowserSetup.exe`（安装器）及 3 个依赖 DLL。

## 技术栈

| 组件 | 说明 |
|---|---|
| 内核 | Microsoft Edge WebView2（Chromium），系统自带运行时 |
| 框架 | C# WinForms（.NET Framework 4.8，系统自带） |
| 编译 | Windows 自带 `csc.exe`，无需 Visual Studio |
| 打包 | 自研 C# 安装器，无第三方打包工具 |
| 依赖 DLL | WebView2 官方 SDK 3 个文件（Core / WinForms / Loader），共约 880KB |

详见《开发计划书.md》。
