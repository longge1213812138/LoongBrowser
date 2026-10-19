// StartupE2ETest.cs —— 启动页端到端测试（真实 WebView2 内核 + 真实 MainForm）
//
// 验证的是用户能看见的那一层：
//   1. 三种启动模式各自真的打开对应页面（空白页 / 书签墙 / 指定网站）；
//   2. 无论启动页设成什么，工具栏「＋新标签」永远显示书签墙；
//   3. startup.json 里的设置能跨"重启"（重开窗口）保留。
//
// 隔离：进程内先把 APPDATA / LOCALAPPDATA 指到临时目录，再碰 AppPaths / MainForm，
// 因此不会读写用户真实的书签、历史与内核数据。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LoongBrowser
{
    public static class StartupE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;
        private static string _base;
        private static string _markerFile;
        private static Timer _watchdog;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _base = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "startup_e2e");
            try { if (Directory.Exists(_base)) Directory.Delete(_base, true); } catch (Exception) { }
            Directory.CreateDirectory(_base);

            // 必须在任何 AppPaths 访问之前设好覆盖（Environment.GetFolderPath 不吃
            // APPDATA 环境变量，所以只能走 AppPaths.RootOverride 这条测试专用通道）
            AppPaths.RootOverride = _base;

            string appData = Path.Combine(_base, "Roaming", "LoongBrowser");
            Directory.CreateDirectory(appData);

            _sb.AppendLine("=== 启动页端到端测试（真实内核 + 真实 MainForm）===");
            _sb.AppendLine("隔离数据目录: " + AppPaths.DataDir);
            _sb.AppendLine("隔离内核目录: " + AppPaths.UserDataDir);
            if (AppPaths.DataDir.IndexOf(_base, StringComparison.OrdinalIgnoreCase) != 0)
            {
                _sb.AppendLine("[FAIL] 数据目录隔离失败，中止（避免写用户真实数据）");
                Console.WriteLine(_sb.ToString());
                return 1;
            }
            _sb.AppendLine();

            // 用本地文件当"指定网站"，避免依赖网络
            _markerFile = Path.Combine(_base, "startup-marker.html");
            File.WriteAllText(_markerFile,
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>STARTUP-MARKER</title></head>" +
                "<body><h1 id=\"m\">startup target</h1></body></html>");
            string markerUrl = new Uri(_markerFile).AbsoluteUri;
            _sb.AppendLine("指定网站 = " + markerUrl);
            _sb.AppendLine();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _watchdog = new Timer();
            _watchdog.Interval = 180000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "整体超时"); Finish(); };
            _watchdog.Start();

            var ctx = new ApplicationContext();
            var form = new Form();
            form.ShowInTaskbar = false;
            form.Location = new System.Drawing.Point(-4000, -4000);
            form.Size = new System.Drawing.Size(100, 100);
            form.Load += delegate { RunAll(ctx, markerUrl); };
            form.Show();
            Application.Run(ctx);

            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void RunAll(ApplicationContext ctx, string markerUrl)
        {
            try
            {
                // 埋两个书签：书签墙要靠它们渲染卡片
                SeedBookmarks();

                _sb.AppendLine("-- A. 启动模式 = 空白页（默认）--");
                WriteSettings(StartupPageSettings.ModeBlank, null);
                var w1 = await OpenWindow();
                CoreWebView2 c1 = await WaitCore(w1);
                if (c1 == null) { Judge("A0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                await Task.Delay(600);
                Judge("A1 地址是 about:blank", IsBlank(SourceOf(c1)), "实际: " + SourceOf(c1));
                Judge("A2 不是书签墙（没有卡片）", await EvalInt(c1, "document.querySelectorAll('.card').length") == 0,
                      "实际卡片数: " + await EvalInt(c1, "document.querySelectorAll('.card').length"));
                Judge("A3 标签页标题不是新标签页", TitleOf(c1) != NewTabPage.NewTabDocumentTitle,
                      "实际: " + TitleOf(c1));

                _sb.AppendLine();
                _sb.AppendLine("-- B. 「＋新标签」与启动模式无关，永远是书签墙 --");
                object tm = TabManagerOf(w1);
                CallNewTab(tm, null);
                CoreWebView2 c1b = await WaitCore(w1);
                await WaitCards(c1b, 2);
                Judge("B1 新标签打开后标题是「新标签页」", TitleOf(c1b) == NewTabPage.NewTabDocumentTitle,
                      "实际: " + TitleOf(c1b));
                Judge("B2 新标签渲染出 2 张卡片", await EvalInt(c1b, "document.querySelectorAll('.card').length") == 2,
                      "实际: " + await EvalInt(c1b, "document.querySelectorAll('.card').length"));
                Judge("B3 启动页是空白页时也不影响新标签",
                      SourceOf(c1b).IndexOf("marker", StringComparison.OrdinalIgnoreCase) < 0, "实际: " + SourceOf(c1b));
                CloseWindow(w1);

                _sb.AppendLine();
                _sb.AppendLine("-- C. 启动模式 = 书签墙 --");
                WriteSettings(StartupPageSettings.ModeWall, null);
                var w2 = await OpenWindow();
                CoreWebView2 c2 = await WaitCore(w2);
                if (c2 == null) { Judge("C0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                await WaitCards(c2, 2);
                Judge("C1 启动即书签墙（2 张卡片）", await EvalInt(c2, "document.querySelectorAll('.card').length") == 2,
                      "实际: " + await EvalInt(c2, "document.querySelectorAll('.card').length"));
                Judge("C2 页头写「启动页」", await EvalStr(c2, "document.querySelector('.ttl').textContent") == NewTabPage.StartupTitle,
                      "实际: " + await EvalStr(c2, "document.querySelector('.ttl') ? document.querySelector('.ttl').textContent : '(无)'"));
                Judge("C3 标签页标题也是「启动页」", TitleOf(c2) == NewTabPage.StartupTitle, "实际: " + TitleOf(c2));
                Judge("C4 地址仍是内部页（不暴露本地路径）", IsBlank(SourceOf(c2)), "实际: " + SourceOf(c2));
                Judge("C5 书签墙的点击桥还在", await EvalInt(c2, "document.querySelectorAll('a.card[data-url]').length") == 2, "");

                // 书签变化后刷新：启动页不能被刷成"新标签页"
                NewTabPage.Enabled = true;
                object tm2 = TabManagerOf(w2);
                InvokeTm(tm2, "RefreshNewTabPages");
                await Task.Delay(700);
                Judge("C6 刷新后仍叫「启动页」（标题不被改写）",
                      await EvalStr(c2, "document.querySelector('.ttl').textContent") == NewTabPage.StartupTitle,
                      "实际: " + await EvalStr(c2, "document.querySelector('.ttl').textContent"));
                CloseWindow(w2);

                _sb.AppendLine();
                _sb.AppendLine("-- D. 启动模式 = 指定网站 --");
                WriteSettings(StartupPageSettings.ModeUrl, markerUrl);
                var w3 = await OpenWindow();
                CoreWebView2 c3 = await WaitCore(w3);
                if (c3 == null) { Judge("D0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                CoreWebView2 c3n = await WaitNavigated(c3, "startup-marker", 15000);
                Judge("D1 真的打开了指定网站", SourceOf(c3n).IndexOf("startup-marker", StringComparison.OrdinalIgnoreCase) >= 0,
                      "实际: " + SourceOf(c3n));
                Judge("D2 页面内容已渲染", await EvalStr(c3n, "document.getElementById('m') ? document.getElementById('m').textContent : ''") == "startup target",
                      "实际: " + await EvalStr(c3n, "document.getElementById('m') ? document.getElementById('m').textContent : ''"));
                Judge("D3 启动页不再是书签墙", await EvalInt(c3n, "document.querySelectorAll('.card').length") == 0, "");

                _sb.AppendLine();
                _sb.AppendLine("-- E. 指定网站模式下，「＋新标签」仍走书签墙 --");
                object tm3 = TabManagerOf(w3);
                CallNewTab(tm3, null);
                CoreWebView2 c3b = await WaitCore(w3);
                await WaitCards(c3b, 2);
                Judge("E1 新标签仍是书签墙", TitleOf(c3b) == NewTabPage.NewTabDocumentTitle, "实际: " + TitleOf(c3b));
                Judge("E2 新标签渲染出 2 张卡片", await EvalInt(c3b, "document.querySelectorAll('.card').length") == 2,
                      "实际: " + await EvalInt(c3b, "document.querySelectorAll('.card').length"));
                CloseWindow(w3);

                _sb.AppendLine();
                _sb.AppendLine("-- F. 设置在重启后保留 --");
                StartupPageSettings.Mode = StartupPageSettings.ModeBlank;   // 清内存，模拟新进程
                StartupPageSettings.Url = null;
                StartupPageSettings.Load();
                Judge("F1 重读后模式仍是 url", StartupPageSettings.Mode == StartupPageSettings.ModeUrl,
                      "实际: " + StartupPageSettings.Mode);
                Judge("F2 重读后地址不变", StartupPageSettings.Url == markerUrl,
                      "实际: " + StartupPageSettings.Url);
                var w4 = await OpenWindow();
                CoreWebView2 c4 = await WaitCore(w4);
                CoreWebView2 c4n = await WaitNavigated(c4, "startup-marker", 15000);
                Judge("F3 重启后仍打开指定网站", SourceOf(c4n).IndexOf("startup-marker", StringComparison.OrdinalIgnoreCase) >= 0,
                      "实际: " + SourceOf(c4n));
                CloseWindow(w4);

                Finish();
            }
            catch (Exception ex)
            {
                Judge("异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        // ---------- 环境准备 ----------

        private static void SeedBookmarks()
        {
            var bm = new BookmarkStore();
            bm.Items = new List<BookmarkItem>();
            bm.Items.Add(new BookmarkItem { Title = "启动测试站点 A", Url = "https://startup-a.invalid/" });
            bm.Items.Add(new BookmarkItem { Title = "启动测试站点 B", Url = "https://startup-b.invalid/" });
            bm.Save();
        }

        private static void WriteSettings(string mode, string url)
        {
            StartupPageSettings.Save(mode, url);
        }

        private static MainForm _current;

        private static async Task<MainForm> OpenWindow()
        {
            var f = new MainForm(null);
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new System.Drawing.Point(-4000, -4000);
            f.Show();
            f.BringToFront();
            _current = f;
            await Task.Delay(300);
            return f;
        }

        private static void CloseWindow(MainForm f)
        {
            try { if (f != null) f.Close(); } catch (Exception) { }
            _current = null;
        }

        // ---------- 反射取 MainForm 内部件 ----------

        private static object TabManagerOf(MainForm f)
        {
            FieldInfo fi = typeof(MainForm).GetField("_tabMgr", BindingFlags.Instance | BindingFlags.NonPublic);
            return fi == null ? null : fi.GetValue(f);
        }

        private static void CallNewTab(object tm, string url)
        {
            MethodInfo mi = tm.GetType().GetMethod("NewTab", new Type[] { typeof(string) });
            if (mi != null) mi.Invoke(tm, new object[] { url });
        }

        private static void InvokeTm(object tm, string method)
        {
            MethodInfo mi = tm.GetType().GetMethod(method, Type.EmptyTypes);
            if (mi != null) mi.Invoke(tm, null);
        }

        // ---------- 内核读取 ----------

        private static async Task<CoreWebView2> WaitCore(MainForm f)
        {
            for (int i = 0; i < 80; i++)
            {
                await Task.Delay(200);
                try
                {
                    MethodInfo mi = TabManagerOf(f).GetType().GetMethod("ActiveCore", Type.EmptyTypes);
                    var core = mi == null ? null : mi.Invoke(TabManagerOf(f), null) as CoreWebView2;
                    if (core != null) return core;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static async Task<CoreWebView2> WaitNavigated(CoreWebView2 core, string needle, int ms)
        {
            for (int i = 0; i < ms / 200; i++)
            {
                await Task.Delay(200);
                string s = SourceOf(core);
                if (s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await Task.Delay(500);
                    return core;
                }
            }
            return core;
        }

        private static async Task WaitCards(CoreWebView2 core, int want)
        {
            for (int i = 0; i < 40; i++)
            {
                int n = await EvalInt(core, "document.querySelectorAll('.card').length");
                if (n == want) return;
                await Task.Delay(200);
            }
        }

        private static string SourceOf(CoreWebView2 core)
        {
            try { return core.Source ?? ""; } catch (Exception) { return ""; }
        }

        private static string TitleOf(CoreWebView2 core)
        {
            try { return core.DocumentTitle ?? ""; } catch (Exception) { return ""; }
        }

        private static bool IsBlank(string src)
        {
            return string.IsNullOrEmpty(src) || src == "about:blank";
        }

        private static async Task<int> EvalInt(CoreWebView2 core, string js)
        {
            try
            {
                string r = await core.ExecuteScriptAsync(js);
                if (r == null) return -1;
                r = r.Trim().Trim('"');
                int v;
                return int.TryParse(r, out v) ? v : -1;
            }
            catch (Exception) { return -1; }
        }

        private static async Task<string> EvalStr(CoreWebView2 core, string js)
        {
            try
            {
                string r = await core.ExecuteScriptAsync(js);
                if (r == null) return null;
                r = r.Trim();
                if (r.Length >= 2 && r.StartsWith("\"") && r.EndsWith("\""))
                {
                    r = r.Substring(1, r.Length - 2);
                    r = r.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
                }
                return r;
            }
            catch (Exception) { return null; }
        }

        // ---------- 断言 ----------

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }

        private static void Finish()
        {
            try { if (_watchdog != null) _watchdog.Stop(); } catch (Exception) { }
            try { if (_current != null) _current.Close(); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Application.ExitThread();
        }
    }
}
