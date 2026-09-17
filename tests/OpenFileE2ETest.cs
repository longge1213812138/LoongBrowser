// OpenFileE2ETest.cs —— 端到端复现与验证：用真实 WebView2 内核分别导航
//   旧逻辑拼出的 URL（"https://" + 裸路径）  → 期望失败（主机名无法解析）
//   修复后 NormalizeUrl(裸路径)              → 期望成功加载本地 HTML
// 这直接对应"双击 .html 文件 → 默认程序打开"的完整链路。
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public static class OpenFileE2ETest
    {
        private static Form _form;
        private static WebView2 _view;
        private static Timer _watchdog;
        private static Timer _titleTimer;
        private static int _stage;
        private static int _pass;
        private static int _fail;
        private static string _file;
        private static string _legacyUrl;
        private static string _fixedUrl;
        private static readonly StringBuilder _sb = new StringBuilder();

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string tmp = Path.Combine(baseDir, "tmp");
            _file = Path.Combine(tmp, "e2e_double_click.html");
            try
            {
                Directory.CreateDirectory(tmp);
                File.WriteAllText(_file,
                    "<!doctype html><html><head><meta charset=\"utf-8\"><title>E2E_OPEN_OK</title></head>" +
                    "<body><h1>local file opened</h1></body></html>");
            }
            catch (Exception ex)
            {
                Console.WriteLine("准备测试文件失败: " + ex.Message);
                return 1;
            }

            // 旧逻辑：把裸路径当网址补上 https://  ← 报 ERR_NAME_NOT_RESOLVED 的那一步
            _legacyUrl = "https://" + _file;
            // 修复后：识别为本地路径并转成 file:/// URL
            _fixedUrl = MainForm.NormalizeUrl(_file);

            string ud = Path.Combine(baseDir, "e2e_ud");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);   // 挪到屏幕外，不打扰用户
            _form.Size = new Size(900, 700);

            _view = new WebView2();
            _view.Dock = DockStyle.Fill;
            _form.Controls.Add(_view);

            _watchdog = new Timer();
            _watchdog.Interval = 45000;
            _watchdog.Tick += delegate { Fail("E0 看门狗超时（45s），测试环境可能无法创建内核"); Finish(); };
            _watchdog.Start();

            _titleTimer = new Timer();
            _titleTimer.Interval = 700;
            _titleTimer.Tick += delegate { _titleTimer.Stop(); CheckTitle(); Finish(); };

            _form.Load += delegate { Boot(ud); };
            _form.Show();
            Application.Run(_form);

            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void Boot(string userDataDir)
        {
            _sb.AppendLine("=== 双击打开本地 HTML 端到端测试（真实 WebView2 内核）===");
            _sb.AppendLine("测试文件: " + _file);
            _sb.AppendLine();
            try
            {
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                await _view.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                Fail("E0 内核初始化失败：" + ex.GetType().Name + " " + ex.Message);
                Finish();
                return;
            }

            _sb.AppendLine("内核版本: " + _view.CoreWebView2.Environment.BrowserVersionString);
            _sb.AppendLine();
            _sb.AppendLine("--- Step1 旧逻辑目标（模拟修复前的双击）---");
            _sb.AppendLine("URL: " + _legacyUrl);
            _view.CoreWebView2.NavigationCompleted += OnNavDone;
            _stage = 1;
            _view.CoreWebView2.Navigate(_legacyUrl);
        }

        private static void OnNavDone(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            string status = e.WebErrorStatus.ToString();

            if (_stage == 1)
            {
                _sb.AppendLine("IsSuccess=" + e.IsSuccess + "  WebErrorStatus=" + status);
                Check("E1 旧逻辑打开失败（复现用户的故障）", !e.IsSuccess);
                Check("E2 错误类型为主机名无法解析（对应 ERR_NAME_NOT_RESOLVED）",
                      status.IndexOf("HostNameNotResolved", StringComparison.OrdinalIgnoreCase) >= 0);
                _sb.AppendLine();
                _sb.AppendLine("--- Step2 修复后目标 ---");
                _sb.AppendLine("URL: " + _fixedUrl);
                _stage = 2;
                _view.CoreWebView2.Navigate(_fixedUrl);
                return;
            }

            if (_stage == 2)
            {
                string src = "";
                try { src = _view.CoreWebView2.Source; } catch (Exception) { }
                _sb.AppendLine("IsSuccess=" + e.IsSuccess + "  WebErrorStatus=" + status);
                _sb.AppendLine("Source=" + src);
                Check("E3 修复后导航成功", e.IsSuccess);
                Check("E4 停在本地文件页（未跳到搜索/DNS 失败页）",
                      src.IndexOf("e2e_double_click.html", StringComparison.OrdinalIgnoreCase) >= 0);
                _stage = 3;
                _titleTimer.Start();   // 等 700ms 让文档标题就绪
            }
        }

        private static void CheckTitle()
        {
            string title = "";
            try { title = _view.CoreWebView2.DocumentTitle; } catch (Exception) { }
            _sb.AppendLine("DocumentTitle=" + title);
            Check("E5 本地 HTML 内容真的渲染了", title == "E2E_OPEN_OK");
        }

        private static void Check(string name, bool ok)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
        }

        private static void Fail(string msg)
        {
            _fail++;
            _sb.AppendLine("[FAIL] " + msg);
        }

        private static void Finish()
        {
            try { _watchdog.Stop(); } catch (Exception) { }
            try { _titleTimer.Stop(); } catch (Exception) { }
            try { _form.Close(); } catch (Exception) { }
        }
    }
}
