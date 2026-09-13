// Program.cs —— 程序入口：单实例互斥 + 命令行 URL 转发（默认浏览器调用时传入）
// 设计：同一时间只允许一个浏览器实例（避免多实例争抢 WebView2 用户数据目录）。
// 后续启动的实例把要打开的 URL 写入传递文件后退出，由运行中的实例轮询接收。
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class Program
    {
        private static Mutex _mutex;

        /// <summary>实例间传递 URL 的文件路径</summary>
        public static string PendingUrlFile
        {
            get { return Path.Combine(AppPaths.DataDir, "open_url.txt"); }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string url = null;
            if (args != null && args.Length > 0 && args[0].Trim().Length > 0)
                url = MainForm.NormalizeUrl(args[0]);

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
            Application.Run(new MainForm(url));

            try { _mutex.ReleaseMutex(); _mutex.Dispose(); } catch (Exception) { }
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
