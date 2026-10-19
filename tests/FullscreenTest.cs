// FullscreenTest.cs —— 两套全屏的单元 / 集成测试（真实 MainForm + 真实内核，不联网）
//
// 用户 2026-10 报的问题：视频全屏有边框。根因是窗口留了一圈 6px 的缩放热区
// 当 Padding，进全屏时没归零，WebView2 被 Dock 挤在里面，四周露出 6px 玻璃。
//
// 本测试锁三件事：
//   A. 全屏形态推导：两个布尔状态各自的 Kind / IsFullscreen / IsPageFullscreen
//   B. 视频真全屏：Padding 归零、WebView2 铺满整块屏幕、标题栏/外壳/标签栏全隐、
//      窗口覆盖 Screen.Bounds（含任务栏）、置顶；退出后逐项还原
//   C. 网页全屏：Padding 同样归零、但标题栏保留、窗口只最大化到工作区（任务栏可见）、
//      不置顶；WebView2 留在 TabPage 里（不搬家）
//   D. 两套叠加：网页全屏中进视频全屏，退出视频后网页全屏原样保留
using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public static class FullscreenTest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static MainForm _form;
        private static TabControl _tabs;
        private static WebView2 _view;
        private static TitleBar _titleBar;
        private static GlassChrome _chrome;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            _sb.AppendLine("=== 两套全屏（视频真全屏 / 网页全屏）===");

            try
            {
                // 内核目录挪到临时目录，别碰用户真实的 Cookie / 缓存
                AppPaths.RootOverride = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "loong_fs_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));

                _form = new MainForm(null);
                _form.ShowInTaskbar = false;
                _form.StartPosition = FormStartPosition.Manual;
                _form.Location = new Point(120, 90);
                _form.Size = new Size(1000, 700);
                _form.Opacity = 0;
                _form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);

                // 从 MainForm 里取真控件，别自己另建一套 —— 要测的就是真实结构
                _tabs = (TabControl)Field(_form, "_tabs");
                _titleBar = (TitleBar)Field(_form, "_titleBar");
                _chrome = (GlassChrome)Field(_form, "_chrome");

                _sb.AppendLine();
                _sb.AppendLine("-- X. 初始状态 --");
                Judge("X1 不在任何全屏", FullscreenManager.Kind == FullscreenKind.None,
                      FullscreenManager.Kind.ToString());
                Judge("X2 缩放热区 Padding 已就位",
                      _form.Padding.Left == VistaTheme.ResizeBorderWidth, _form.Padding.ToString());
                Judge("X3 标题栏可见", _titleBar != null && _titleBar.Visible, "");
                Judge("X4 外壳可见", _chrome != null && _chrome.Visible, "");

                // 等第一个标签的内核起来（MainForm 构造时已经 NewTab 了）
                _view = WaitActiveView(40);
                if (_view == null)
                {
                    Judge("X5 拿到 WebView2", false, "没拿到活动标签的 WebView2");
                    return Finish();
                }
                Judge("X5 拿到 WebView2", true, "");

                // ---------- B. 视频真全屏 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- B. 视频真全屏（Fullscreen API）--");
                Rectangle savedBounds = _form.Bounds;
                Padding savedPadding = _form.Padding;
                bool savedTopMost = _form.TopMost;
                bool savedTitleBar = _titleBar.Visible;
                Control originalParent = _view.Parent;

                Invoke("EnterFullscreen", _view);

                Screen scr = Screen.FromControl(_form);
                Judge("B1 形态 = Video", FullscreenManager.Kind == FullscreenKind.Video,
                      FullscreenManager.Kind.ToString());
                Judge("B2 IsFullscreen = true", FullscreenManager.IsFullscreen, "");
                Judge("B3 IsPageFullscreen = false（两套不串）", !FullscreenManager.IsPageFullscreen(_form), "");
                Judge("B4 Padding 归零（边框的根因）",
                      _form.Padding == Padding.Empty, _form.Padding.ToString());
                Judge("B5 窗口覆盖整块屏幕（含任务栏）",
                      _form.Bounds == scr.Bounds, _form.Bounds + " vs " + scr.Bounds);
                Judge("B6 置顶", _form.TopMost, "");
                Judge("B7 无边框", _form.FormBorderStyle == FormBorderStyle.None,
                      _form.FormBorderStyle.ToString());
                Judge("B8 标题栏隐藏", !_titleBar.Visible, "");
                Judge("B9 外壳隐藏", !_chrome.Visible, "");
                Judge("B10 标签栏隐藏", !_tabs.Visible, "");
                Judge("B11 WebView2 已挂到 Form 上（脱离标签页）",
                      _view.Parent == _form, Describe(_view.Parent));

                Rectangle client = _view.RectangleToScreen(_view.ClientRectangle);
                Judge("B12 WebView2 铺满整块屏幕（不再有余边）",
                      client == scr.Bounds, client + " vs " + scr.Bounds);

                // ---------- B' 退出还原 ----------
                FullscreenManager.ExitFullscreen();
                Application.DoEvents();

                Judge("B13 形态回到 None", FullscreenManager.Kind == FullscreenKind.None,
                      FullscreenManager.Kind.ToString());
                Judge("B14 Padding 还原", _form.Padding == savedPadding, _form.Padding.ToString());
                Judge("B15 窗口位置大小还原",
                      _form.Bounds == savedBounds, _form.Bounds + " vs " + savedBounds);
                Judge("B16 置顶还原", _form.TopMost == savedTopMost, "");
                Judge("B17 标题栏还原", _titleBar.Visible == savedTitleBar, "");
                Judge("B18 外壳还原", _chrome.Visible, "");
                Judge("B19 标签栏还原", _tabs.Visible, "");
                Judge("B20 WebView2 放回原标签页",
                      _view.Parent == originalParent, Describe(_view.Parent));

                // ---------- C. 网页全屏 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- C. 网页全屏（宿主入口 / F11）--");
                savedBounds = _form.Bounds;
                savedPadding = _form.Padding;

                FullscreenManager.Toggle(_form);
                Application.DoEvents();

                Judge("C1 形态 = Page", FullscreenManager.Kind == FullscreenKind.Page,
                      FullscreenManager.Kind.ToString());
                Judge("C2 IsPageFullscreen = true", FullscreenManager.IsPageFullscreen(_form), "");
                Judge("C3 IsFullscreen = false（不是视频那套）", !FullscreenManager.IsFullscreen, "");
                Judge("C4 Padding 也归零（网页要铺满客户区）",
                      _form.Padding == Padding.Empty, _form.Padding.ToString());
                Judge("C5 外壳隐藏", !_chrome.Visible, "");
                Judge("C6 标题栏保留（与视频全屏的关键区别）", _titleBar.Visible, "");
                Judge("C7 不置顶", !_form.TopMost, "");
                Judge("C8 窗口最大化到工作区",
                      _form.WindowState == FormWindowState.Maximized,
                      _form.WindowState.ToString());
                // 网页全屏也必须把网页抬到 Form 上：实测只藏外壳时 WebView2 是
                // {4,35,2552,1353}（工作区 2560x1392）—— TabControl 自带 4px 边框，
                // 标签头也还占着高度。抬上来之后才真正铺满客户区（标题栏之下）。
                Judge("C9 WebView2 已抬到窗口上（TabControl 的边框会留边）",
                      _view.Parent == _form, Describe(_view.Parent));
                Rectangle pageClient = _view.RectangleToScreen(_view.ClientRectangle);
                Rectangle formClient = _form.RectangleToScreen(_form.ClientRectangle);
                Judge("C9b 网页在标题栏之下铺满整个客户区（左右下三边贴合）",
                      pageClient.Left == formClient.Left &&
                      pageClient.Right == formClient.Right &&
                      pageClient.Bottom == formClient.Bottom &&
                      pageClient.Top == formClient.Top + VistaTheme.TitleBarHeight,
                      pageClient + " vs 客户区 " + formClient);

                Rectangle wa = Screen.FromControl(_form).WorkingArea;
                Judge("C10 最大化不盖任务栏", _form.Bounds.Height <= wa.Height + 2,
                      _form.Bounds + " vs " + wa);

                // ---------- D. 两套叠加 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- D. 网页全屏中进视频全屏（叠加）--");
                Invoke("EnterFullscreen", _view);
                Judge("D1 现在形态 = Video（视频优先）",
                      FullscreenManager.Kind == FullscreenKind.Video, FullscreenManager.Kind.ToString());
                Judge("D2 网页全屏状态仍在（Active 未清）",
                      FullscreenManager.IsPageFullscreen(_form), "");

                FullscreenManager.ExitFullscreen();
                Application.DoEvents();
                Judge("D3 退出视频后形态退回 Page",
                      FullscreenManager.Kind == FullscreenKind.Page, FullscreenManager.Kind.ToString());
                Judge("D4 网页全屏的外壳仍隐藏", !_chrome.Visible, "");
                Judge("D5 退出视频后 Padding 仍归零（网页全屏还在）",
                      _form.Padding == Padding.Empty, _form.Padding.ToString());

                FullscreenManager.Toggle(_form);
                Application.DoEvents();
                Judge("D6 再退网页全屏 → 全部还原", FullscreenManager.Kind == FullscreenKind.None,
                      FullscreenManager.Kind.ToString());
                Judge("D7 Padding 还原", _form.Padding == savedPadding, _form.Padding.ToString());
                Judge("D8 外壳还原", _chrome.Visible, "");
                Judge("D9 窗口状态还原", _form.WindowState == FormWindowState.Normal,
                      _form.WindowState.ToString());

                // ---------- E. 两套互斥性 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- E. 语义边界 --");
                Judge("E1 ExitAll 在无全屏时是空操作", SafeExitAll(), "");
                Judge("E2 仍不在全屏", FullscreenManager.Kind == FullscreenKind.None, "");
            }
            catch (Exception ex)
            {
                Judge("未捕获异常", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { if (_form != null && !_form.IsDisposed) { _form.Opacity = 1; _form.Close(); } } catch (Exception) { }
                try
                {
                    if (AppPaths.RootOverride != null && System.IO.Directory.Exists(AppPaths.RootOverride))
                        System.IO.Directory.Delete(AppPaths.RootOverride, true);
                }
                catch (Exception) { }
            }

            return Finish();
        }

        private static bool SafeExitAll()
        {
            try { FullscreenManager.ExitAll(_form); Application.DoEvents(); return true; }
            catch (Exception) { return false; }
        }

        private static object Field(object obj, string name)
        {
            try
            {
                FieldInfo fi = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                return fi == null ? null : fi.GetValue(obj);
            }
            catch (Exception) { return null; }
        }

        /// <summary>反射调用 FullscreenManager.EnterFullscreen(WebView2)，等价于网页触发 Fullscreen API</summary>
        private static void Invoke(string method, object arg)
        {
            try
            {
                MethodInfo mi = typeof(FullscreenManager).GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (mi == null) { Console.WriteLine("!! 找不到方法 " + method); return; }
                mi.Invoke(null, new object[] { arg });
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! 反射调用 " + method + " 失败: " + ex.Message);
            }
        }

        /// <summary>等第一个标签的 WebView2（MainForm 构造里已经 NewTab，这里只等控件出现）</summary>
        private static WebView2 WaitActiveView(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                try
                {
                    if (_tabs != null && _tabs.TabPages.Count > 0)
                    {
                        foreach (Control c in _tabs.TabPages[0].Controls)
                        {
                            var wv = c as WebView2;
                            if (wv != null) return wv;
                        }
                    }
                }
                catch (Exception) { }
                Application.DoEvents();
                System.Threading.Thread.Sleep(100);
            }
            return null;
        }

        private static string Describe(Control c)
        {
            return c == null ? "null" : c.GetType().Name;
        }

        private static int Finish()
        {
            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
