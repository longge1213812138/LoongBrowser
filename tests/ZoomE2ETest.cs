// ZoomE2ETest.cs —— 网页缩放端到端测试（真实 WebView2 内核 + 真实 TabManager）
//
// 验证链路：TabManager.ZoomIn/ZoomOut/SetZoom/ResetZoom → WebView2.ZoomFactor 真的写进内核；
//           50%~200% 两端封死；新标签页继承会话默认比例；换页（导航）后比例不丢；
//           Ctrl+滚轮用的 ZoomAtPoint 能按屏幕坐标命中标签。
// 键盘/滚轮消息本身（IMessageFilter）依赖真实输入焦点，逻辑判定已由 tests\ZoomTest.cs 单测覆盖。
using System;
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
    public static class ZoomE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static Timer _watchdog;
        private static int _zoomEvents;
        private static string _pageFile;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ud = Path.Combine(baseDir, "e2e_ud_zoom");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _sb.AppendLine("=== 网页缩放端到端测试（真实 WebView2 + 真实 TabManager）===");

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
                TabManager.SetEnvironment(env);
                _sb.AppendLine("内核版本: " + env.BrowserVersionString);
                _sb.AppendLine();

                ZoomManager.SessionDefault = ZoomManager.DefaultZoom;   // 明确起点：100%
                _tm = new TabManager(_tabs);
                _tm.Bookmarks = new BookmarkStore();
                _tm.ZoomChanged += delegate { _zoomEvents++; };
                _tm.NewTab(null);

                CoreWebView2 core = await WaitCore();
                if (core == null) { Judge("A0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                _sb.AppendLine();

                _sb.AppendLine("-- A. 初始比例 --");
                Judge("A1 新标签默认 100%", Pct(_tm.ActiveZoom()) == "100%", Pct(_tm.ActiveZoom()));
                Judge("A2 事件未触发时计数为 0", _zoomEvents == 0, "计数 " + _zoomEvents);

                _sb.AppendLine();
                _sb.AppendLine("-- B. 步进写进真实内核（读回 WebView2.ZoomFactor）--");
                _tm.ZoomIn();
                Judge("B1 放大一级 = 110%", Pct(_tm.ActiveZoom()) == "110%", Pct(_tm.ActiveZoom()));
                Judge("B2 内核侧读回也是 110%", Pct(RawZoom()) == "110%", Pct(RawZoom()));
                _tm.ZoomIn();
                Judge("B3 再放大 = 120%", Pct(_tm.ActiveZoom()) == "120%", Pct(_tm.ActiveZoom()));
                _tm.ZoomOut();
                Judge("B4 缩小一级 = 110%", Pct(_tm.ActiveZoom()) == "110%", Pct(_tm.ActiveZoom()));
                _tm.ResetZoom();
                Judge("B5 恢复默认 = 100%", Pct(_tm.ActiveZoom()) == "100%", Pct(_tm.ActiveZoom()));
                Judge("B6 每次改动都触发 ZoomChanged", _zoomEvents >= 4, "计数 " + _zoomEvents);

                _sb.AppendLine();
                _sb.AppendLine("-- C. 50%~200% 两端封死 --");
                _tm.SetZoom(0.1);
                Judge("C1 设 10% → 收进 50%", Pct(_tm.ActiveZoom()) == "50%", Pct(_tm.ActiveZoom()));
                _tm.SetZoom(9.0);
                Judge("C2 设 900% → 收进 200%", Pct(_tm.ActiveZoom()) == "200%", Pct(_tm.ActiveZoom()));
                for (int i = 0; i < 25; i++) _tm.ZoomIn();
                Judge("C3 反复放大仍停在 200%", Pct(_tm.ActiveZoom()) == "200%", Pct(_tm.ActiveZoom()));
                Judge("C4 内核侧同样是 200%", Pct(RawZoom()) == "200%", Pct(RawZoom()));
                for (int i = 0; i < 25; i++) _tm.ZoomOut();
                Judge("C5 反复缩小仍停在 50%", Pct(_tm.ActiveZoom()) == "50%", Pct(_tm.ActiveZoom()));
                _tm.ResetZoom();
                Judge("C6 重置回 100%", Pct(_tm.ActiveZoom()) == "100%", Pct(_tm.ActiveZoom()));

                _sb.AppendLine();
                _sb.AppendLine("-- D. 预设档位点选（菜单/工具栏走的就是 SetZoom）--");
                _tm.SetZoom(1.5);
                Judge("D1 150% 写入成功", Pct(_tm.ActiveZoom()) == "150%", Pct(_tm.ActiveZoom()));
                _tm.SetZoom(0.67);
                Judge("D2 67% 档位", Pct(_tm.ActiveZoom()) == "67%", Pct(_tm.ActiveZoom()));
                _tm.SetZoom(2.0);
                Judge("D3 200% 档位", Pct(_tm.ActiveZoom()) == "200%", Pct(_tm.ActiveZoom()));

                _sb.AppendLine();
                _sb.AppendLine("-- E. 新标签页继承会话默认比例 --");
                ZoomManager.SessionDefault = 1.5;
                _tm.NewTab(null);
                CoreWebView2 core2 = await WaitCore();
                if (core2 == null) { Judge("E0 第二个标签内核就绪", false, ""); Finish(); return; }
                Judge("E1 新标签 = 150%", Pct(_tm.ActiveZoom()) == "150%", Pct(_tm.ActiveZoom()));

                _sb.AppendLine();
                _sb.AppendLine("-- F. 换页（导航）后比例不丢 --");
                _pageFile = Path.Combine(userDataDir, "zoom_page.html");
                File.WriteAllText(_pageFile, "<html><head><title>Zoom Page</title></head><body><h1>zoom</h1></body></html>");
                string fileUrl = new Uri(_pageFile).AbsoluteUri;
                core2.Navigate(fileUrl);
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(200);
                    string src = "";
                    try { src = core2.Source ?? ""; } catch (Exception) { }
                    if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) break;
                }
                await Task.Delay(500);
                Judge("F1 已导航到本地页面", (core2.Source ?? "").StartsWith("file:", StringComparison.OrdinalIgnoreCase),
                      core2.Source ?? "(空)");
                Judge("F2 导航后仍是 150%", Pct(_tm.ActiveZoom()) == "150%", Pct(_tm.ActiveZoom()));

                _sb.AppendLine();
                _sb.AppendLine("-- G. ZoomAtPoint：按屏幕坐标命中标签（Ctrl+滚轮路径）--");
                WebView2 view = _tm.ActiveView();
                Rectangle rect = view.RectangleToScreen(view.ClientRectangle);
                Point inside = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                Point outside = new Point(rect.Right + 5000, rect.Bottom + 5000);

                bool hitOut = _tm.ZoomAtPoint(outside, 1);
                Judge("G1 视野外 → 未命中", !hitOut, "hit=" + hitOut);
                Judge("G2 未命中时比例不变", Pct(_tm.ActiveZoom()) == "150%", Pct(_tm.ActiveZoom()));

                bool hitIn = _tm.ZoomAtPoint(inside, 1);
                Judge("G3 标签内 → 命中", hitIn, "hit=" + hitIn);
                Judge("G4 命中后放大一级 = 160%", Pct(_tm.ActiveZoom()) == "160%", Pct(_tm.ActiveZoom()));

                // 回归：未选中的标签在同一块屏幕上，缩放绝不能落到它头上
                WebView2 bg = _tabs.TabPages[0].Controls[0] as WebView2;
                Judge("G3b 隐藏标签未被波及（仍 200%）",
                      bg != null && Pct(ZoomManager.FactorOf(bg)) == "200%",
                      bg == null ? "取不到隐藏标签" : Pct(ZoomManager.FactorOf(bg)));

                _tm.ZoomAtPoint(inside, -2);
                Judge("G5 连续下滚两格 = 140%", Pct(_tm.ActiveZoom()) == "140%", Pct(_tm.ActiveZoom()));

                _tm.ZoomAtPoint(inside, -50);
                Judge("G6 狂滚不越界，停在 50%", Pct(_tm.ActiveZoom()) == "50%", Pct(_tm.ActiveZoom()));

                _tm.ResetZoom();
                Judge("G7 重置回 100%", Pct(_tm.ActiveZoom()) == "100%", Pct(_tm.ActiveZoom()));
                Judge("G8 全程 ZoomChanged 事件计数", _zoomEvents >= 10, "计数 " + _zoomEvents);

                Finish();
            }
            catch (Exception ex)
            {
                Judge("执行未抛异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        // ---------- 工具 ----------

        private static string Pct(double f)
        {
            return ZoomManager.Percent(f);
        }

        /// <summary>绕过 TabManager，直接读内核控制器上的比例</summary>
        private static double RawZoom()
        {
            try
            {
                WebView2 v = _tm.ActiveView();
                if (v == null || v.CoreWebView2 == null) return -1;
                return ZoomManager.Round(v.ZoomFactor);
            }
            catch (Exception) { return -1; }
        }

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

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
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
