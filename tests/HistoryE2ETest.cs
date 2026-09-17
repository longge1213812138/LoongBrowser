// HistoryE2ETest.cs —— 浏览历史端到端测试（真实 WebView2 + 真实 TabManager）
// 验证"真的访问一个网站会记进历史、并带上页面标题"，以及内部页不记、新→旧排序。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public static class HistoryE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static HistoryStore _history;
        private static Timer _watchdog;
        private static string _dir;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _dir = Path.Combine(baseDir, "hist_e2e");
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (Exception) { }
            Directory.CreateDirectory(_dir);
            HistoryStore.FilePath = Path.Combine(_dir, "history.json");

            _sb.AppendLine("=== 浏览历史端到端测试（真实 WebView2 + 真实 TabManager）===");
            _sb.AppendLine("历史文件: " + HistoryStore.FilePath);
            _sb.AppendLine();

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);
            _form.Size = new Size(1000, 720);
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _form.Controls.Add(_tabs);

            _watchdog = new Timer();
            _watchdog.Interval = 150000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "整体超时"); Finish(); };
            _watchdog.Start();

            _form.Load += delegate { Boot(Path.Combine(baseDir, "e2e_ud_hist")); };
            _form.Show();
            Application.Run(_form);

            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void Boot(string userDataDir)
        {
            try
            {
                try { if (Directory.Exists(userDataDir)) Directory.Delete(userDataDir, true); } catch (Exception) { }
                var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir, opts);
                TabManager.SetEnvironment(env);
                _sb.AppendLine("内核版本: " + env.BrowserVersionString);
                _sb.AppendLine();

                _history = new HistoryStore();
                var bookmarks = new BookmarkStore();
                bookmarks.Items = new List<BookmarkItem>();

                _sb.AppendLine("-- A. 访问真实站点会记进历史 --");
                _tm = new TabManager(_tabs);
                _tm.Bookmarks = bookmarks;
                _tm.History = _history;
                _tm.NewTab("https://example.com/");

                CoreWebView2 core = await WaitCore();
                if (core == null) { Judge("A0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                if (!await WaitUrl(core, "example.com", 30000))
                {
                    Info("A0 跳过：无法打开 example.com（网络受限）");
                    Finish();
                    return;
                }
                await Task.Delay(1500);      // 等标题/记录落盘

                Judge("A1 访问后历史里有 1 条", _history.Items.Count == 1, "实际 " + _history.Items.Count);
                if (_history.Items.Count > 0)
                {
                    HistoryItem it = _history.Items[0];
                    Judge("A2 记的是真实地址", it.Url.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0,
                          "实际: " + it.Url);
                    Judge("A3 带上了页面标题（不是裸网址）",
                          !string.IsNullOrEmpty(it.Title) &&
                          !string.Equals(it.Title, it.Url, StringComparison.OrdinalIgnoreCase),
                          "实际标题: " + it.Title);
                    long age = HistoryStore.Now() - it.VisitedAt;
                    Judge("A4 时间戳是刚才", age >= 0 && age < 60, "距今 " + age + " 秒");
                    Info("A5 记录: " + it.Title + " | " + it.Url + " | " +
                         HistoryStore.ToLocal(it.VisitedAt).ToString("HH:mm:ss"));
                }
                _sb.AppendLine();

                _sb.AppendLine("-- B. 内部页不记 --");
                int before = _history.Items.Count;
                _tm.NewTab(null);            // 新标签页（书签墙）
                await Task.Delay(2000);
                Judge("B1 打开新标签页不产生历史", _history.Items.Count == before,
                      "实际 " + _history.Items.Count);
                _sb.AppendLine();

                _sb.AppendLine("-- C. 新→旧排列与去重 --");
                _tm.NewTab("https://example.org/");
                if (!await WaitUrlAny("example.org", 30000)) Info("C0 提示：example.org 打开超时");
                await Task.Delay(1500);
                Judge("C1 第二次访问追加记录", _history.Items.Count == before + 1,
                      "实际 " + _history.Items.Count);
                if (_history.Items.Count >= 2)
                {
                    Judge("C2 最新的排在最前",
                          _history.Items[0].Url.IndexOf("example.org", StringComparison.OrdinalIgnoreCase) >= 0,
                          "Items[0]=" + _history.Items[0].Url);
                    Judge("C3 早一次的排在后面",
                          _history.Items[1].Url.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0,
                          "Items[1]=" + _history.Items[1].Url);
                }

                // 紧接着重复访问同一地址（模拟刷新）→ 不新增
                int n = _history.Items.Count;
                CoreWebView2 c2 = await WaitCore();
                try { c2.Reload(); } catch (Exception) { }
                await Task.Delay(2500);
                Judge("C4 刷新同一页面不新增记录（去重生效）", _history.Items.Count == n,
                      "实际 " + _history.Items.Count);

                Judge("C5 历史已落盘", File.Exists(HistoryStore.FilePath), HistoryStore.FilePath);
                _sb.AppendLine();

                _sb.AppendLine("-- D. 删除与清空 --");
                if (_history.Items.Count > 0)
                {
                    int m = _history.Items.Count;
                    _history.Remove(_history.Items[0]);
                    Judge("D1 单条删除生效", _history.Items.Count == m - 1, "实际 " + _history.Items.Count);
                }
                _history.Clear();
                Judge("D2 一键清空生效", _history.Items.Count == 0, "实际 " + _history.Items.Count);
                var reloaded = new HistoryStore();
                Judge("D3 清空已写回磁盘", reloaded.Items.Count == 0, "实际 " + reloaded.Items.Count);

                Finish();
            }
            catch (Exception ex)
            {
                Judge("异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        private static async Task<CoreWebView2> WaitCore()
        {
            for (int i = 0; i < 80; i++)
            {
                await Task.Delay(200);
                CoreWebView2 core = null;
                try { core = _tm.ActiveCore(); } catch (Exception) { }
                if (core != null) return core;
            }
            return null;
        }

        private static async Task<bool> WaitUrl(CoreWebView2 core, string part, int ms)
        {
            int loops = Math.Max(1, ms / 250);
            for (int i = 0; i < loops; i++)
            {
                string s = "";
                try { s = core.Source ?? ""; } catch (Exception) { }
                if (s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                await Task.Delay(250);
            }
            return false;
        }

        /// <summary>当前标签的地址里出现某段（用于等待第二个站点打开）</summary>
        private static async Task<bool> WaitUrlAny(string part, int ms)
        {
            int loops = Math.Max(1, ms / 250);
            for (int i = 0; i < loops; i++)
            {
                string s = "";
                try { var c = _tm.ActiveCore(); if (c != null) s = c.Source ?? ""; } catch (Exception) { }
                if (s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                await Task.Delay(250);
            }
            return false;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            _sb.AppendLine("       * " + s);
        }

        private static void Finish()
        {
            try { if (_watchdog != null) _watchdog.Stop(); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            try { if (_form != null) _form.Close(); } catch (Exception) { }
        }
    }
}
