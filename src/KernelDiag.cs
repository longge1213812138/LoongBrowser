// KernelDiag.cs —— 内核初始化诊断：把 WebView2 启动失败的完整事实落到日志里
//
// 为什么需要它：WebView2 的失败原因高度分散（SDK 版本、运行时版本、用户数据目录、
// 残留内核进程、沙箱/权限），而 csc 构建没有任何调试附属信息。上一版把
// "环境创建失败" 和 "控制器初始化失败" 合并成一句话，排查时等于没有信息。
//
// 这个模块只做两件事：收集环境事实、把异常连同 HResult 一起写文件。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace LoongBrowser
{
    /// <summary>内核启动诊断：只读收集 + 落盘，不改变任何行为</summary>
    public static class KernelDiag
    {
        private static readonly object _lock = new object();

        /// <summary>诊断日志路径：%APPDATA%\LoongBrowser\kernel.log</summary>
        public static string LogPath
        {
            get { return Path.Combine(AppPaths.DataDir, "kernel.log"); }
        }

        /// <summary>
        /// 记录一条"正常路径"的痕迹。默认**不落盘**——正常启动不该往用户盘上写噪声，
        /// 只有真正出错时 <see cref="Write"/> 才写完整事实。
        /// 需要排查时把 EnableTrace 置 true，或设环境变量 LOONGBROWSER_KDIAG=1 即可打开。
        /// </summary>
        public static void Trace(string message)
        {
            if (!EnableTrace) return;
            Write("[trace] " + message);
        }

        /// <summary>是否记录正常路径的痕迹（默认关闭；环境变量 LOONGBROWSER_KDIAG=1 可开启）</summary>
        public static bool EnableTrace
        {
            get
            {
                if (_forceTrace.HasValue) return _forceTrace.Value;
                bool on = false;
                try
                {
                    string v = Environment.GetEnvironmentVariable("LOONGBROWSER_KDIAG");
                    on = !string.IsNullOrEmpty(v) && v != "0";
                }
                catch (Exception) { }
                _forceTrace = on;
                return on;
            }
            set { _forceTrace = value; }
        }

        private static bool? _forceTrace;

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    AppPaths.EnsureDataDir();
                    // 日志超过 256 KB 就滚动一次，避免无限增长
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 256 * 1024)
                    {
                        string old = LogPath + ".1";
                        try { File.Delete(old); } catch (Exception) { }
                        try { File.Move(LogPath, old); } catch (Exception) { }
                    }
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception) { }
        }

        /// <summary>把异常连同 HResult 与内部异常一起展开成一行可读文本</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return "(null)";
            var sb = new StringBuilder();
            int depth = 0;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (depth > 0) sb.Append(" <- ");
                sb.Append(e.GetType().Name);
                sb.Append(" 0x").Append(e.HResult.ToString("X8"));
                sb.Append("：").Append(e.Message);
                depth++;
                if (depth >= 4) break;
            }
            return sb.ToString();
        }

        /// <summary>收集当前环境事实（SDK 版本、运行时版本、数据目录状态）</summary>
        public static string CollectFacts(string userDataDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---- 环境事实 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ----");
            sb.AppendLine("进程位数: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
            sb.AppendLine("SDK 版本: " + SdkVersion());
            sb.AppendLine("数据目录: " + userDataDir);
            sb.AppendLine("数据目录存在: " + SafeExists(userDataDir));
            sb.AppendLine("数据目录可写: " + SafeWritable(userDataDir));
            sb.AppendLine("lockfile 存在: " + SafeExists(Path.Combine(userDataDir, "EBWebView", "lockfile")));
            sb.AppendLine("EBWebView 存在: " + SafeExists(Path.Combine(userDataDir, "EBWebView")));
            return sb.ToString();
        }

        /// <summary>读取本进程加载的 WebView2 Core 程序集版本（即项目 libs/ 里那份 SDK）</summary>
        public static string SdkVersion()
        {
            try
            {
                var asm = typeof(CoreWebView2Environment).Assembly;
                return asm.GetName().Version + " (" + asm.Location + ")";
            }
            catch (Exception) { return "(未知)"; }
        }

        /// <summary>读取已安装的 WebView2 运行时版本；未安装或读取失败返回 null</summary>
        public static string RuntimeVersion()
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception) { return null; }
        }

        private static string SafeExists(string path)
        {
            try { return Directory.Exists(path) ? "是" : "否"; } catch (Exception) { return "(读不到)"; }
        }

        private static string SafeWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write_probe_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return "是";
            }
            catch (Exception ex) { return "否 (" + ex.GetType().Name + ")"; }
        }

        /// <summary>构建给用户看的失败提示：一句话结论 + 可复制的细节</summary>
        public static string BuildUserMessage(string stage, Exception ex, string userDataDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("浏览器内核初始化失败（阶段：" + stage + "）。");
            sb.AppendLine();
            if (ex != null)
            {
                sb.AppendLine("错误：" + Describe(ex));
                sb.AppendLine();
                string hint = HintFor(ex);
                if (hint != null)
                {
                    sb.AppendLine("可能原因：" + hint);
                    sb.AppendLine();
                }
            }
            sb.AppendLine("诊断日志：" + LogPath);
            sb.AppendLine("SDK 版本：" + SdkVersion());
            string rt = RuntimeVersion();
            sb.AppendLine("运行时版本：" + (rt ?? "未检测到 / 读取失败"));
            return sb.ToString();
        }

        /// <summary>把常见 HResult 翻译成可执行的建议</summary>
        private static string HintFor(Exception ex)
        {
            int hr = ex != null ? ex.HResult : 0;

            if (hr == unchecked((int)0x800700AA))   // ERROR_BUSY
                return "内核拒绝启动（资源被占用）。常见于：上一次的内核进程未完全退出、" +
                       "或 WebView2 SDK 与已安装的运行时不匹配。可先结束残留的 msedgewebview2.exe 后重试。";

            if (hr == unchecked((int)0x80070005))   // E_ACCESSDENIED
                return "数据目录没有写权限。检查 %LOCALAPPDATA%\\LoongBrowser\\WebView2 的访问权限。";

            if (hr == unchecked((int)0x80070020))   // ERROR_SHARING_VIOLATION
                return "数据目录被另一个进程占用。请确认没有多份 LoongBrowser 在同时运行。";

            if (hr == unchecked((int)0x80070490) || hr == unchecked((int)0x80070002))
                return "未找到 WebView2 运行时，或运行时文件不完整。建议重装 WebView2 Runtime。";

            return null;
        }
    }
}
