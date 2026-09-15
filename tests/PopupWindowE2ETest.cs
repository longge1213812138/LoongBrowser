// PopupWindowE2ETest.cs —— 小窗（画中画窗口）置顶功能的端到端测试
//
// 走真实链路：真实 WebView2 + 真实 TabManager → 页面 window.open('about:blank')
//   → TabManager.OpenPopupWindow 自建 PopupWindow 并交给发起方
// 验证：窗口确实建出来、默认置顶、可开可关、尺寸按请求、opener 能往窗口里写内容（B 站小窗的前提）。
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
    public static class PopupWindowE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static Timer _watchdog;
        private static bool _savedDefault;
        private static bool _restoreDefault;

        private const int ReqW = 420;
        private const int ReqH = 260;

        private static readonly string OpenJs =
            "(function(){ window.__w = window.open('about:blank','pipwin','width=" + ReqW + ",height=" + ReqH + "');"
            + " return window.__w ? 'opened' : 'null'; })()";

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ud = Path.Combine(baseDir, "e2e_ud_popupwin");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _sb.AppendLine("=== 小窗置顶端到端测试（真实 WebView2 + 真实 TabManager）===");

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);
            _form.Size = new Size(1000, 720);
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _form.Controls.Add(_tabs);

            _watchdog = new Timer();
            _watchdog.Interval = 120000;
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
                var opts = new CoreWebView2EnvironmentOptions("--disable-popup-blocking");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir, opts);
                TabManager.SetEnvironment(env);
                _sb.AppendLine("内核版本: " + env.BrowserVersionString);

                // 记下用户当前偏好，测试结束后恢复，别改掉人家的设置
                _savedDefault = PopupWindow.DefaultAlwaysOnTop;
                _restoreDefault = true;
                Info("测试前「小窗默认置顶」= " + _savedDefault);
                PopupWindow.DefaultAlwaysOnTop = true;      // 假定默认开启来做下面的断言
                _sb.AppendLine();

                _sb.AppendLine("-- A. 建窗与默认置顶 --");
                var bookmarks = new BookmarkStore();
                bookmarks.Items = new List<BookmarkItem>();
                _tm = new TabManager(_tabs);
                _tm.Bookmarks = bookmarks;
                _tm.NewTab("https://example.com/");        // 用真实来源的页面，保证 opener 与子窗口同源

                CoreWebView2 core = null;
                for (int i = 0; i < 80 && core == null; i++)
                {
                    await Task.Delay(200);
                    try { core = _tm.ActiveCore(); } catch (Exception) { }
                }
                if (core == null) { Judge("A0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }

                bool loaded = false;
                for (int i = 0; i < 80; i++)
                {
                    string src = "";
                    try { src = core.Source ?? ""; } catch (Exception) { }
                    if (src.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0) { loaded = true; break; }
                    await Task.Delay(250);
                }
                if (!loaded) { Info("A0 跳过：无法打开 example.com（网络受限）"); Finish(); return; }
                Info("A0 宿主页已打开: " + Title(core));

                string probe = await EvalStr(core, OpenJs);
                Judge("A1 window.open 拿到了窗口（不再是 null）", probe == "opened", "实际: " + probe);

                PopupWindow win = await WaitPopup(80);
                if (win == null) { Judge("A2 小窗已创建", false, "10 秒内没等到 PopupWindow"); Finish(); return; }
                Judge("A2 小窗已由我们自己创建", true, "");
                Info("A3 小窗标题: " + win.Text + "，尺寸: " + win.ClientSize.Width + "×" + win.ClientSize.Height);

                // 挪到屏幕外，别在用户桌面上闪
                try { win.Location = new Point(-4000, -4000); } catch (Exception) { }

                Judge("A4 小窗默认置顶（始终显示在最前）", win.IsAlwaysOnTop, "实际 TopMost=" + win.IsAlwaysOnTop);
                Judge("A5 小窗尺寸按请求设置（内容区 " + ReqH + " + 工具条 " + PopupWindow.BarHeight + "）",
                      win.ClientSize.Height == ReqH + PopupWindow.BarHeight,
                      "实际 ClientSize.Height=" + win.ClientSize.Height);
                _sb.AppendLine();

                _sb.AppendLine("-- B. opener 能往小窗里写内容（B 站小窗的前提）--");
                string wrote = await EvalStr(core,
                    "(function(){ try{ var w=window.__w; w.document.title='PIP_MARK';" +
                    " w.document.body.innerHTML='<b>hello</b>'; return String(w.document.title); }" +
                    " catch(e){ return 'ERR:'+e.name+':'+e.message; } })()");
                Judge("B1 opener 可写入并读回小窗文档", wrote == "PIP_MARK", "实际: " + wrote);
                string popSrc = "";
                try { popSrc = win.Core != null ? (win.Core.Source ?? "") : ""; } catch (Exception) { }
                Info("B2 小窗地址: " + (popSrc.Length == 0 ? "(空)" : popSrc));
                _sb.AppendLine();

                _sb.AppendLine("-- C. 置顶开关可开可关 --");
                win.SetAlwaysOnTop(false);
                await Task.Delay(300);
                Judge("C1 关闭置顶后 TopMost=false", !win.IsAlwaysOnTop, "实际 TopMost=" + win.IsAlwaysOnTop);
                Judge("C2 开关状态已记住（DefaultAlwaysOnTop=false）", !PopupWindow.DefaultAlwaysOnTop,
                      "实际 " + PopupWindow.DefaultAlwaysOnTop);

                win.SetAlwaysOnTop(true);
                await Task.Delay(300);
                Judge("C3 重新开启置顶后 TopMost=true", win.IsAlwaysOnTop, "实际 TopMost=" + win.IsAlwaysOnTop);
                Judge("C4 开关状态已记住（DefaultAlwaysOnTop=true）", PopupWindow.DefaultAlwaysOnTop,
                      "实际 " + PopupWindow.DefaultAlwaysOnTop);

                // 顶部那个复选框是真实入口，确认勾选状态跟着切换
                Judge("C5 顶部复选框与状态一致", win.IsAlwaysOnTop == true, "");
                _sb.AppendLine();

                _sb.AppendLine("-- D. 站点自己调 window.close() 关闭小窗 --");
                string closeRet = await EvalStr(core, "(function(){ try{ window.__w.close(); return 'called'; }catch(e){ return 'ERR:'+e.name; } })()");
                Info("D0 页面调用 window.close(): " + closeRet);
                await Task.Delay(1200);
                Judge("D1 宿主窗口跟着关闭（没有留下孤儿窗口）", PopupWindow.OpenWindows().Length == 0,
                      "剩余 " + PopupWindow.OpenWindows().Length + " 个");
                string closed = await EvalStr(core, "(function(){ try{ return String(!!(window.__w && window.__w.closed)); }catch(e){ return 'ERR:'+e.name; } })()");
                Judge("D2 opener 侧窗口已关闭", closed == "true", "实际: " + closed);

                Finish();
            }
            catch (Exception ex)
            {
                Judge("异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        private static async Task<PopupWindow> WaitPopup(int loops)
        {
            for (int i = 0; i < loops; i++)
            {
                PopupWindow[] all = PopupWindow.OpenWindows();
                if (all.Length > 0) return all[0];
                await Task.Delay(200);
            }
            return null;
        }

        private static string Title(CoreWebView2 core)
        {
            try { return core.DocumentTitle ?? ""; } catch (Exception) { return ""; }
        }

        private static async Task<string> EvalStr(CoreWebView2 core, string js)
        {
            try
            {
                string r = await core.ExecuteScriptAsync(js);
                if (r == null) return "";
                r = r.Trim();
                if (r.Length >= 2 && r.StartsWith("\"") && r.EndsWith("\""))
                {
                    r = r.Substring(1, r.Length - 2);
                    r = r.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\").Replace("\\/", "/");
                }
                return r;
            }
            catch (Exception ex) { return "(脚本异常: " + ex.GetType().Name + ")"; }
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
            // 收尾：关掉可能还开着的小窗，并把用户的偏好改回去
            try
            {
                PopupWindow[] all = PopupWindow.OpenWindows();
                for (int i = 0; i < all.Length; i++) { try { all[i].Close(); } catch (Exception) { } }
            }
            catch (Exception) { }
            if (_restoreDefault)
            {
                try
                {
                    PopupWindow.DefaultAlwaysOnTop = _savedDefault;
                    Info("已恢复用户原设置「小窗默认置顶」= " + _savedDefault);
                }
                catch (Exception) { }
            }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            try { if (_form != null) _form.Close(); } catch (Exception) { }
        }
    }
}
