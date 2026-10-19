// Program.cs —— 程序入口：单实例互斥 + 进程内多窗口管理 + URL 转发
// 设计：
//   1. 进程级单实例（Mutex）：避免多进程争抢 WebView2 用户数据目录。
//   2. 进程内支持多个浏览器窗口（右键"在新窗口中打开链接"直接开新窗口）。
//   3. 后续进程启动时把 URL 写入传递文件，由运行中的窗口轮询认领。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>
    /// 应用上下文：管理多个浏览器窗口，最后一个**浏览器窗口**关闭时退出程序。
    ///
    /// 为什么自己记账、而不是看 Application.OpenForms.Count：
    /// 外壳的悬停提示、菜单下拉都是独立的 Form，会一起进 OpenForms。
    /// 用户点关闭时提示窗若还活着（鼠标刚从工具按钮上离开就是这种情形），
    /// Count 不等于 0，ExitThread() 不执行 —— 窗口没了、消息循环还在转，
    /// 进程于是残留；下次启动被单实例互斥体挡住，看起来就是"打不开"。
    /// 判据只该数"浏览器窗口"，不该数"任何窗口"。
    /// </summary>
    public class LoongBrowserContext : ApplicationContext
    {
        private readonly List<Form> _windows = new List<Form>();

        public void OpenWindow(string url)
        {
            var form = new MainForm(url);
            _windows.Add(form);
            form.FormClosed += delegate
            {
                _windows.Remove(form);
                if (_windows.Count == 0) ExitThread();
            };
            form.Show();
        }

        /// <summary>当前打开的浏览器窗口数（测试用）</summary>
        public int WindowCount
        {
            get { return _windows.Count; }
        }
    }

    public static class Program
    {
        private static Mutex _mutex;
        private static LoongBrowserContext _context;

        /// <summary>实例间传递 URL 的文件路径</summary>
        public static string PendingUrlFile
        {
            get { return Path.Combine(AppPaths.DataDir, "open_url.txt"); }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string url = null;
            if (args != null && args.Length > 0)
            {
                // 双击"默认程序打开"时，命令行形如 "C:\dir\my page.html"。
                // 若注册表命令里的 %1 没加引号，含空格的路径会被拆成多个参数，这里重新拼回完整路径。
                string raw = (args.Length == 1) ? args[0] : string.Join(" ", args);
                raw = raw.Trim();
                if (raw.Length > 0) url = MainForm.NormalizeUrl(raw);
            }

            bool createdNew;
            _mutex = new Mutex(true, "LoongBrowser_SingleInstance_Mutex", out createdNew);

            if (!createdNew)
            {
                // 已有实例在运行：把 URL 交给它，然后把它的窗口带到前台，本进程退出
                if (!string.IsNullOrEmpty(url))
                {
                    try
                    {
                        AppPaths.EnsureDataDir();
                        File.WriteAllText(PendingUrlFile, url);
                    }
                    catch (Exception) { }
                }
                ActivateExistingWindow();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 每次启动顺手收拾一下"打开方式"里指向已消失文件的历史条目。
            // 程序挪过位置、旧安装目录被删之后，这些条目会一直挂在右键菜单里，
            // 用户点它没反应 —— 与其让人手动改注册表，不如启动时静默清理一次。
            try { FileAssociations.RemoveStaleRegistrations(); } catch (Exception) { }

            _context = new LoongBrowserContext();
            _context.OpenWindow(url);
            Application.Run(_context);

            try { _mutex.ReleaseMutex(); _mutex.Dispose(); } catch (Exception) { }
        }

        /// <summary>打开一个全新的浏览器窗口（右键"在新窗口中打开链接"用，进程内多窗口）</summary>
        public static void OpenNewWindow(string url)
        {
            if (_context != null) _context.OpenWindow(url);
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                int cur = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("LoongBrowser"))
                {
                    if (p.Id == cur) continue;
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
