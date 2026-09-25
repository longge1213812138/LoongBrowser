// FullscreenE2ETest.cs —— F11 轻量全屏端到端测试（真实主窗口结构 + 真实内核 + 真实 TabManager）
//
// 覆盖三件事（全程不注入任何系统输入：不模拟按键、不动鼠标、不抢焦点）：
//   A/B  Toggle 进出：隐藏菜单与工具栏、窗口最大化，退出后可见性/窗口状态原样还原；
//        标签栏与标题栏按“轻量全屏”的定义保留，且不影响网页真全屏状态
//   C   页面内转发链：AddScriptToExecuteOnDocumentCreatedAsync 注入的脚本监听 F11
//        → window.chrome.webview.postMessage → 宿主切换（合成 KeyboardEvent 触发，
//        这是网页有焦点时 F11 的唯一通道；测试用脚本事件代替真实按键）
//   D   与网页真全屏（Fullscreen API）共存：轻量全屏中进入真全屏，退出真全屏后
//        轻量全屏状态必须原样保留（菜单仍隐藏、窗口仍最大化）
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
    public static class FullscreenE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static MenuStrip _menu;
        private static ToolStrip _toolbar;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static Timer _watchdog;
        private static WebView2 _view;
        private static string _pageFile;
        private static bool _exitRequested;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ud = Path.Combine(baseDir, "e2e_ud_fullscreen");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _sb.AppendLine("=== F11 轻量全屏端到端测试（真实窗口结构 + 真实内核）===");
            Log("=== F11 轻量全屏端到端测试（真实窗口结构 + 真实内核）===");
            Log("[STEP] Main: 构造窗口");

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);   // 屏幕外
            _form.Size = new Size(1000, 720);
            _form.Opacity = 0;                          // 测试会最大化窗口，别遮住用户桌面
            _form.Text = "FullscreenE2E";

            _menu = new MenuStrip();
            _menu.Items.Add("文件(&F)");
            _toolbar = new ToolStrip();
            _toolbar.Items.Add(new ToolStripButton("地址"));
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;

            _form.Controls.Add(_tabs);
            _form.Controls.Add(_toolbar);
            _form.Controls.Add(_menu);

            FullscreenManager.Init(_form, _menu, _toolbar, _tabs);

            _watchdog = new Timer();
            _watchdog.Interval = 90000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "整体超时"); Finish(); };
            _watchdog.Start();

            _form.Load += delegate { Boot(ud); };
            _form.Show();
            Log("[STEP] 窗口已显示，进入消息循环");
            // Boot 可能在 Show 期间就失败并请求退出：那时窗体已 Close，
            // 再 Run 一个已关闭的窗体会永远不返回（看门狗又已停）→ 必须跳过
            if (!_exitRequested)
            {
                Application.Run(_form);
                Log("[STEP] 消息循环结束");
            }
            else
            {
                Log("[STEP] 已提前退出，不再进入消息循环");
            }

            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void Boot(string userDataDir)
        {
            try
            {
                Log("[STEP] Boot 开始");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                TabManager.SetEnvironment(env);
                Log("[STEP] 内核环境就绪 " + env.BrowserVersionString);
                _sb.AppendLine("内核版本: " + env.BrowserVersionString);
                _sb.AppendLine();

                _tm = new TabManager(_tabs);
                Log("[STEP] TabManager 就绪");
                _tm.Bookmarks = new BookmarkStore();
                Log("[STEP] BookmarkStore 就绪");

                _pageFile = Path.Combine(userDataDir, "fullscreen_page.html");
                Directory.CreateDirectory(userDataDir);     // 内核环境此时可能还没落盘建目录
                File.WriteAllText(_pageFile,
                    "<html><head><title>FS</title></head><body><h1>fullscreen</h1></body></html>");
                Log("[STEP] 测试页已写入 " + _pageFile);
                _tm.NewTab(new Uri(_pageFile).AbsoluteUri);
                Log("[STEP] NewTab 已调用，等内核");

                CoreWebView2 core = await WaitCore();
                if (core == null) { Judge("X0 内核就绪", false, "拿不到 CoreWebView2"); Finish(); return; }
                Log("[STEP] 内核就绪");
                _view = _tm.ActiveView();
                Phase("-- X. 初始状态 --");
                Judge("X1 Init 后 KeyPreview=true（宿主侧 F11 入口的前提）", _form.KeyPreview, "");
                Judge("X2 菜单可见", _menu.Visible, "");
                Judge("X3 工具栏可见", _toolbar.Visible, "");
                Judge("X4 标签栏可见", _tabs.Visible, "");
                Judge("X5 非轻量全屏", !FullscreenManager.IsUiFullscreen(_form), "");
                Judge("X6 非网页真全屏", !FullscreenManager.IsFullscreen, "");

                Phase("-- A. 进入 F11 轻量全屏 --");
                FullscreenManager.Toggle(_form);
                Judge("A1 已进入轻量全屏", FullscreenManager.IsUiFullscreen(_form), "");
                Judge("A2 菜单隐藏", !_menu.Visible, "Visible=" + _menu.Visible);
                Judge("A3 工具栏隐藏", !_toolbar.Visible, "Visible=" + _toolbar.Visible);
                Judge("A4 标签栏保留可见", _tabs.Visible, "Visible=" + _tabs.Visible);
                Judge("A5 窗口最大化", _form.WindowState == FormWindowState.Maximized,
                      _form.WindowState.ToString());
                Judge("A6 标题栏保留（边框样式不变）",
                      _form.FormBorderStyle == FormBorderStyle.Sizable, _form.FormBorderStyle.ToString());
                Judge("A7 与网页真全屏状态互不干扰", !FullscreenManager.IsFullscreen, "");

                Phase("-- B. 退出并还原 --");
                FullscreenManager.Toggle(_form);
                Judge("B1 已退出轻量全屏", !FullscreenManager.IsUiFullscreen(_form), "");
                Judge("B2 菜单恢复可见", _menu.Visible, "Visible=" + _menu.Visible);
                Judge("B3 工具栏恢复可见", _toolbar.Visible, "Visible=" + _toolbar.Visible);
                Judge("B4 标签栏仍可见", _tabs.Visible, "");
                Judge("B5 窗口恢复 Normal", _form.WindowState == FormWindowState.Normal,
                      _form.WindowState.ToString());

                Phase("-- C. 页面内转发的 F11（注入脚本 → postMessage → 宿主）--");
                string hooked = await Eval(core, "!!window.__loongF11");
                Judge("C1 脚本已注入到页面", hooked == "true", "实际 " + hooked);

                await SendPageKey(core, "F11");
                Judge("C2 页内 F11 → 进入轻量全屏", FullscreenManager.IsUiFullscreen(_form), "");
                Judge("C3 菜单随之隐藏", !_menu.Visible, "Visible=" + _menu.Visible);

                await SendPageKey(core, "F11");
                Judge("C4 再按一次 → 退出", !FullscreenManager.IsUiFullscreen(_form), "");
                Judge("C5 菜单随之恢复", _menu.Visible, "Visible=" + _menu.Visible);

                await SendPageKey(core, "A");
                Judge("C6 其它按键不触发", !FullscreenManager.IsUiFullscreen(_form), "");

                Phase("-- D. 与网页真全屏共存（反射模拟 ContainsFullScreenElementChanged 链）--");
                FullscreenManager.Toggle(_form);            // 先进轻量全屏
                Judge("D1 轻量全屏已就位（菜单=隐藏）", !_menu.Visible && FullscreenManager.IsUiFullscreen(_form), "");

                bool invoked = InvokeEnterFullscreen(_view);
                Judge("D2 模拟网页触发真全屏", invoked && FullscreenManager.IsFullscreen, "");
                Judge("D3 真全屏隐藏标签栏", !_tabs.Visible, "Visible=" + _tabs.Visible);
                Judge("D4 真全屏隐藏菜单", !_menu.Visible, "");
                Judge("D5 真全屏：边框变为 None", _form.FormBorderStyle == FormBorderStyle.None,
                      _form.FormBorderStyle.ToString());
                Judge("D6 轻量全屏状态仍在（两套独立）", FullscreenManager.IsUiFullscreen(_form), "");

                FullscreenManager.ExitFullscreen();
                Judge("D7 真全屏已退出", !FullscreenManager.IsFullscreen, "");
                Judge("D8 标签栏恢复可见", _tabs.Visible, "Visible=" + _tabs.Visible);
                Judge("D9 边框样式恢复", _form.FormBorderStyle == FormBorderStyle.Sizable,
                      _form.FormBorderStyle.ToString());
                Judge("D10 轻量全屏状态被保留：菜单仍隐藏", !_menu.Visible, "Visible=" + _menu.Visible);
                Judge("D11 轻量全屏状态被保留：窗口仍最大化",
                      _form.WindowState == FormWindowState.Maximized, _form.WindowState.ToString());
                Judge("D12 WebView2 已放回标签页", _view.Parent == _tabs.SelectedTab,
                      _view.Parent == null ? "null" : _view.Parent.GetType().Name);

                FullscreenManager.Toggle(_form);            // 最后再退轻量全屏
                Judge("D13 全部还原：菜单可见", _menu.Visible, "");
                Judge("D14 全部还原：窗口 Normal", _form.WindowState == FormWindowState.Normal,
                      _form.WindowState.ToString());

                Finish();
            }
            catch (Exception ex)
            {
                Judge("执行未抛异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        // ---------- 工具 ----------

        /// <summary>阶段标记：同时进结果和控制台（卡住时能看最后一步）</summary>
        private static void Phase(string s)
        {
            _sb.AppendLine();
            _sb.AppendLine(s);
            Log(s);
        }

        private static void Log(string s)
        {
            try { Console.WriteLine(s); Console.Out.Flush(); } catch (Exception) { }
        }

        /// <summary>在页面里派发一个合成 keydown（等价于“网页有焦点时按了键”，但不碰系统输入）</summary>
        private static async Task SendPageKey(CoreWebView2 core, string key)
        {
            await core.ExecuteScriptAsync(
                "document.dispatchEvent(new KeyboardEvent('keydown',{key:'" + key + "',bubbles:true}))");
            await Task.Delay(500);      // postMessage → 宿主处理是异步的
        }

        /// <summary>反射调用私有 EnterFullscreen(WebView2)，等价于 ContainsFullScreenElementChanged 触发</summary>
        private static bool InvokeEnterFullscreen(WebView2 view)
        {
            try
            {
                MethodInfo mi = typeof(FullscreenManager).GetMethod("EnterFullscreen",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (mi == null) return false;
                mi.Invoke(null, new object[] { view });
                return true;
            }
            catch (Exception ex)
            {
                _sb.AppendLine("       * EnterFullscreen 反射调用失败：" + ex.Message);
                return false;
            }
        }

        private static async Task<string> Eval(CoreWebView2 core, string js)
        {
            try
            {
                string r = await core.ExecuteScriptAsync(js);
                return r == null ? "null" : r.Trim();
            }
            catch (Exception ex) { return "(异常 " + ex.GetType().Name + ")"; }
        }

        private static async Task<CoreWebView2> WaitCore()
        {
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                CoreWebView2 core = null;
                try { core = _tm == null ? null : _tm.ActiveCore(); } catch (Exception) { }
                if (core != null) return core;
            }
            return null;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            string line = (ok ? "[PASS] " : "[FAIL] ") + name;
            _sb.AppendLine(line);
            Log(line);
            if (!ok && !string.IsNullOrEmpty(detail))
            {
                _sb.AppendLine("       " + detail);
                Log("       " + detail);      // 失败详情也即时打出，避免挂住时看不到原因
            }
        }

        private static void Finish()
        {
            Log("[STEP] Finish");
            _exitRequested = true;
            try { if (_watchdog != null) _watchdog.Stop(); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            try
            {
                if (_form != null && !_form.IsDisposed && _form.IsHandleCreated) _form.Close();
            }
            catch (Exception) { }
        }
    }
}
