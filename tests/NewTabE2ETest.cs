// NewTabE2ETest.cs —— 新标签页端到端测试（真实 WebView2 内核 + 真实 TabManager 接线）
//
// 验证链路：TabManager.NewTab(null) → ShowNewTabPage → BuildHtml → NavigateToString
//           → 页面渲染卡片 → 点击卡片 postMessage → 宿主校验后 Navigate
// 同时验证两条边界：未收藏的地址必须被拒绝；图标能产出 32×32 PNG 缓存。
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
    public static class NewTabE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static BookmarkStore _bookmarks;
        private static List<BookmarkItem> _list;
        private static string _tmpIcon;
        private static readonly List<string> _tmpFiles = new List<string>();
        private static string _lastMessage;
        private static Timer _watchdog;

        private static string _urlA = "https://e2e-newtab.invalid/a";
        private static string _urlB = "https://e2e-newtab-b.invalid/b";
        private static string _notBookmarked = "https://not-bookmarked.invalid/evil";

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ud = Path.Combine(baseDir, "e2e_ud_newtab");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _sb.AppendLine("=== 新标签页端到端测试（真实 WebView2 + 真实 TabManager）===");

            // 准备书签：A 有图标（测试前塞进缓存），B 没有图标
            _list = new List<BookmarkItem>();
            _list.Add(new BookmarkItem { Title = "测试站点 A", Url = _urlA });
            _list.Add(new BookmarkItem { Title = "测试站点 B", Url = _urlB });
            _tmpIcon = FaviconCache.IconPath(_urlA);
            bool iconSeeded = SeedIcon(_tmpIcon);
            _sb.AppendLine("埋入测试图标: " + iconSeeded + "  ->  " + _tmpIcon);
            _sb.AppendLine();

            _bookmarks = new BookmarkStore();
            _bookmarks.Items = _list;

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);
            _form.Size = new Size(1000, 720);
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _form.Controls.Add(_tabs);

            _watchdog = new Timer();
            _watchdog.Interval = 90000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "整体超时"); Finish(); };
            _watchdog.Start();

            _form.Load += delegate { Boot(ud); };
            _form.Show();
            Application.Run(_form);

            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void Boot(string userDataDir)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                TabManager.SetEnvironment(env);          // 用隔离的用户数据目录，避免干扰真实浏览器数据
                _sb.AppendLine("内核版本: " + env.BrowserVersionString);
                _sb.AppendLine();

                _sb.AppendLine("-- A. 新标签页渲染 --");
                _tm = new TabManager(_tabs);
                _tm.Bookmarks = _bookmarks;
                _tm.NewTab(null);                        // 真实入口：空 url → 书签墙

                CoreWebView2 core = await WaitCore();
                if (core == null) { Judge("A0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }

                core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
                {
                    try { _lastMessage = e.TryGetWebMessageAsString(); } catch (Exception) { }
                };

                int cards = await WaitInt(core, "document.querySelectorAll('.card').length", 2);
                Judge("A1 渲染出 2 张书签卡片", cards == 2, "实际 " + cards);
                Judge("A2 真实图标用 img（1 张）",
                      await EvalInt(core, "document.querySelectorAll('img.ic').length") == 1, "");
                Judge("A3 无图标书签用共享空白图标（1 个 .icb）",
                      await EvalInt(core, "document.querySelectorAll('.icb').length") == 1, "");
                Judge("A3b 默认不再用首字母色块",
                      await EvalInt(core, "document.querySelectorAll('.ph').length") == 0, "");

                string t1 = await EvalStr(core, "document.querySelectorAll('.card .t')[0].textContent");
                Judge("A4 卡片标题正确", t1 == "测试站点 A", "实际: " + t1);

                string icon = await EvalStr(core, "document.querySelector('img.ic') ? document.querySelector('img.ic').src.slice(0,22) : ''");
                Judge("A5 图标以 data:URI 内联", icon == "data:image/png;base64,", "实际: " + icon);

                string blankFile = FaviconCache.DefaultIconFile;
                Judge("A5b 空白图标文件已生成", blankFile != null && File.Exists(blankFile), "路径: " + blankFile);

                string bg = await EvalStr(core,
                    "(function(){var e=document.querySelector('.icb');if(!e)return '';return getComputedStyle(e).backgroundImage.slice(0,40);})()");
                Judge("A5c 空白图标由 CSS 背景提供",
                      bg != null && bg.IndexOf("data:image/png", StringComparison.Ordinal) >= 0, "实际: " + bg);
                string dt = "";
                try { dt = core.DocumentTitle; } catch (Exception) { }
                Judge("A6 页面标题", dt == "新标签页", "实际: " + dt);

                string src = "";
                try { src = core.Source ?? ""; } catch (Exception) { }
                Judge("A7 地址保持内部页（about:blank）", NewTabPage.IsInternalUrl(src), "实际: " + src);
                _sb.AppendLine();

                _sb.AppendLine("-- B. 点击桥：未收藏地址必须被拒绝 --");
                _lastMessage = null;
                await core.ExecuteScriptAsync("window.chrome.webview.postMessage('" + _notBookmarked + "'); 1");
                await Task.Delay(700);
                string after = "";
                try { after = core.Source ?? ""; } catch (Exception) { }
                Judge("B1 未收藏地址不触发导航", NewTabPage.IsInternalUrl(after), "实际 Source: " + after);
                _sb.AppendLine();

                _sb.AppendLine("-- C. 点击桥：点击卡片跳转 --");
                _lastMessage = null;
                await core.ExecuteScriptAsync("document.querySelectorAll('.card')[0].click(); 1");
                await Task.Delay(900);
                Judge("C1 宿主收到 postMessage", _lastMessage == _urlA, "实际: " + _lastMessage);
                string nav = "";
                try { nav = core.Source ?? ""; } catch (Exception) { }
                Judge("C2 宿主据此发起导航", nav.IndexOf("e2e-newtab.invalid", StringComparison.OrdinalIgnoreCase) >= 0,
                      "实际 Source: " + nav);
                _sb.AppendLine();

                _sb.AppendLine("-- D. 图标来源（官方接口 / 兜底抓取）--");
                await TestIconSource(core);

                Finish();
            }
            catch (Exception ex)
            {
                Judge("异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        /// <summary>验证图标能真正抓到并归一化成 32×32 PNG（网络不可用时按跳过处理，不算失败）</summary>
        private static async Task TestIconSource(CoreWebView2 core)
        {
            string[] hosts = new string[] { "www.bing.com", "github.com", "www.baidu.com" };
            string picked = null;

            // 清掉这几个站点的图标缓存，保证"官方通道"这次真的会被走到（只删这几个哈希，不动整个缓存目录）
            for (int i = 0; i < hosts.Length; i++)
            {
                try
                {
                    string p = FaviconCache.IconPath("https://" + hosts[i] + "/x");
                    if (p != null && File.Exists(p)) File.Delete(p);
                }
                catch (Exception) { }
            }

            // 先做一张诊断表：哪些站点能直接抓到 /favicon.ico、返回了什么
            for (int i = 0; i < hosts.Length; i++)
            {
                string h = hosts[i];
                string diag = RawProbe(h);
                Info("D0 " + h + " → " + diag);
                if (picked == null && diag.StartsWith("OK", StringComparison.Ordinal)) picked = h;
            }

            if (picked == null)
            {
                Info("D1 跳过：本环境抓不到任何站点图标（不影响其它用例）");
                return;
            }

            // 官方通道：真的访问一次站点，等 FaviconChanged 把图标落盘
            string path = FaviconCache.IconPath("https://" + picked + "/x");
            await ProbeOfficial(core, picked);
            Info("D1 官方通道诊断: 事件触发 " + _favEvents + " 次, FaviconUri=" +
                 (_favUri == null ? "(空)" : _favUri) + ", 取图结果=" + _favResult);

            // 站点可能重定向（www.bing.com → cn.bing.com），官方图标会记在"最终页面"的域名下
            string effectiveHost = null;
            try { effectiveHost = FaviconCache.HostOf(core.Source); } catch (Exception) { }
            string effectivePath = (effectiveHost == null) ? null
                : FaviconCache.IconPath("https://" + effectiveHost + "/x");
            bool officialOk = (effectivePath != null) && File.Exists(effectivePath);
            Info("D1 导航后实际域名=" + (effectiveHost == null ? "(空)" : effectiveHost) +
                 "，书签域名=" + picked + "，官方通道产出=" + officialOk);
            Judge("D1 官方通道(FaviconChanged)产出 PNG", officialOk, "实际未产出");

            // 兜底通道：书签域名侧直接抓 /favicon.ico（覆盖重定向导致域名不一致的情况）
            if (!File.Exists(path))
            {
                FaviconCache.FetchRemote("https://" + picked + "/x", null);
                for (int i = 0; i < 40 && !File.Exists(path); i++) await Task.Delay(250);
                Info("D1 兜底通道(FetchRemote)产出: " + File.Exists(path));
            }
            Judge("D2 兜底通道抓到书签域名的图标", File.Exists(path), "路径: " + path);

            string verify = File.Exists(path) ? path : (officialOk ? effectivePath : null);
            if (verify == null)
            {
                Info("D3 跳过：没有任何图标文件可校验");
                return;
            }
            try
            {
                using (var bmp = new Bitmap(verify))
                    Judge("D3 归一化尺寸为 " + FaviconCache.IconSize + "×" + FaviconCache.IconSize,
                          bmp.Width == FaviconCache.IconSize && bmp.Height == FaviconCache.IconSize,
                          "实际 " + bmp.Width + "×" + bmp.Height);
            }
            catch (Exception ex)
            {
                Judge("D3 图标可解码", false, ex.Message);
            }
            FileInfo fi = new FileInfo(verify);
            Info("D4 单个图标 " + fi.Length + " 字节（内联成 data:URI 后约 " + (fi.Length * 4 / 3) + " 字节）");

            // 站点明确没有图标（404）→ 应落一个空白图标，避免每次开新标签页都重试
            string noIco = "example.com";
            string diag2 = RawProbe(noIco);
            Info("D5 " + noIco + " → " + diag2);
            if (diag2.StartsWith("FAIL", StringComparison.Ordinal) && diag2.IndexOf("404", StringComparison.Ordinal) >= 0)
            {
                string np = FaviconCache.IconPath("https://" + noIco + "/x");
                try { if (File.Exists(np)) File.Delete(np); } catch (Exception) { }
                FaviconCache.FetchRemote("https://" + noIco + "/x", null);
                for (int i = 0; i < 40 && !File.Exists(np); i++) await Task.Delay(250);
                Judge("D5 404 站点被记成空白图标", File.Exists(np), "路径: " + np);
                if (File.Exists(np))
                {
                    using (var bmp = new Bitmap(np))
                        Judge("D6 空白图为 " + FaviconCache.IconSize + "×" + FaviconCache.IconSize,
                              bmp.Width == FaviconCache.IconSize && bmp.Height == FaviconCache.IconSize,
                              bmp.Width + "×" + bmp.Height);
                    _tmpFiles.Add(np);
                }
            }
            else
            {
                Info("D5 跳过：该站点未返回 404（" + diag2 + "）");
            }
        }

        private static int _favEvents;
        private static string _favUri;
        private static string _favResult = "未触发";

        /// <summary>官方通道插桩：监听 FaviconChanged，记录 FaviconUri 与 GetFaviconAsync 的结果</summary>
        private static async Task ProbeOfficial(CoreWebView2 core, string host)
        {
            _favEvents = 0;
            _favUri = null;
            _favResult = "未触发";
            core.FaviconChanged += delegate(object s, object e)
            {
                _favEvents++;
                try { _favUri = core.FaviconUri; } catch (Exception) { }
                if (!string.IsNullOrEmpty(_favUri)) FetchFaviconInline(core);
            };
            try { core.Navigate("https://" + host + "/"); } catch (Exception) { }
            for (int i = 0; i < 80 && _favResult == "未触发"; i++) await Task.Delay(250);
        }

        private static async void FetchFaviconInline(CoreWebView2 core)
        {
            try
            {
                using (Stream s = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                {
                    if (s == null) { _favResult = "GetFaviconAsync 返回 null"; return; }
                    byte[] buf = new byte[256 * 1024];
                    int n = s.Read(buf, 0, buf.Length);
                    _favResult = "成功，PNG " + n + " 字节";
                }
            }
            catch (Exception ex)
            {
                _favResult = "异常 " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>直接抓一次 /favicon.ico 并报告结果，用于判断"网络不通"还是"代码有问题"</summary>
        private static string RawProbe(string host)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://" + host + "/favicon.ico");
                req.Method = "GET";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.AllowAutoRedirect = true;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LoongBrowser";
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    byte[] buf = new byte[8];
                    int n = s.Read(buf, 0, buf.Length);
                    var sb = new StringBuilder();
                    for (int i = 0; i < n; i++) sb.Append(buf[i].ToString("x2"));
                    return "OK " + (int)resp.StatusCode + " " + resp.ContentType + " 首字节=" + sb;
                }
            }
            catch (Exception ex)
            {
                return "FAIL " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        // ---------- 工具 ----------

        private static async Task<CoreWebView2> WaitCore()
        {
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                CoreWebView2 core = null;
                try { core = _tm.ActiveCore(); } catch (Exception) { }
                if (core != null) return core;
            }
            return null;
        }

        private static async Task<int> WaitInt(CoreWebView2 core, string js, int want)
        {
            for (int i = 0; i < 40; i++)
            {
                int v = await EvalInt(core, js);
                if (v == want) return v;
                if (v >= 0 && i > 6) return v;
                await Task.Delay(250);
            }
            return await EvalInt(core, js);
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

        private static bool SeedIcon(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var bmp = new Bitmap(FaviconCache.IconSize, FaviconCache.IconSize))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(255, 220, 60, 60));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                return File.Exists(path);
            }
            catch (Exception) { return false; }
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
            try { if (!string.IsNullOrEmpty(_tmpIcon) && File.Exists(_tmpIcon)) File.Delete(_tmpIcon); } catch (Exception) { }
            for (int i = 0; i < _tmpFiles.Count; i++)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_tmpFiles[i]) && File.Exists(_tmpFiles[i])) File.Delete(_tmpFiles[i]);
                }
                catch (Exception) { }
            }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            try { if (_form != null) _form.Close(); } catch (Exception) { }
        }
    }
}
