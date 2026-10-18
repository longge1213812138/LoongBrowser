// FullscreenManager.cs —— 全屏管理：网页真全屏（Fullscreen API） + F11 轻量全屏
//
// 两套全屏共存、互不影响（用户选定）：
//   1) 网页真全屏：网页调用标准 Fullscreen API（视频播放器的「全屏」按钮）
//      → ContainsFullScreenElementChanged → 把 WebView2 从 TabPage 摘到 Form 上、
//        隐藏菜单 / 工具栏 / 标签栏、无边框铺满整屏并置顶；Esc / F11 退出，一切复原。
//      bilibili 说明：「网页全屏」按钮是站点自己的 CSS 变换，不走 Fullscreen API，不在此列。
//   2) F11 轻量全屏（本次新增）：用户自己按 F11 → 只隐藏菜单与工具栏、窗口最大化；
//      保留标题栏、标签栏与任务栏。再按 F11 退出，把可见性与窗口状态原样还原。
//
// F11 的两条入口（互斥，同一次按键只会走其中一条）：
//   · 焦点在地址栏等宿主控件 → WinForms KeyDown（Init 里开了 KeyPreview）
//   · 焦点在网页里 → 由 AddScriptToExecuteOnDocumentCreatedAsync 注入的页面脚本监听 keydown，
//     再 window.chrome.webview.postMessage 回宿主。
//     背景：网页有焦点时，按键消息的接收方是 WebView2 内核**进程**里的窗口，不会进宿主
//     的消息队列；不引入系统级钩子，页面内转发就是唯一的通道 —— 与新标签页点击桥同一套机制。
//
// 多窗口：每个 Form 一份 UiState，F11 只作用于按键来源的那个窗口；关窗时自动清理。
//
// 共存时的状态关系（已由端到端测试覆盖）：
//   轻量全屏中进入真全屏 → 真全屏按当时的可见性（菜单/工具栏=隐藏）保存；
//   退出真全屏 → 菜单/工具栏仍然隐藏、窗口仍最大化，轻量全屏状态原样保留。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    /// <summary>全屏管理器</summary>
    public static class FullscreenManager
    {
        /// <summary>网页 Fullscreen API 触发的真全屏是否生效</summary>
        public static bool IsFullscreen { get; private set; }

        /// <summary>注入到每个页面里的 F11 转发脚本（网页有焦点时把按键报给宿主，
        /// 与新标签页点击桥同一套机制，不碰系统输入）</summary>
        private const string HotkeyScript =
            "(function(){" +
            "  if (window.__loongF11) return; window.__loongF11 = true;" +
            "  window.addEventListener('keydown', function(e){" +
            "    if (e.key === 'F11' || e.keyCode === 122) {" +
            "      try { window.chrome.webview.postMessage('loong:f11'); } catch (err) {}" +
            "    }" +
            "  }, true);" +
            "})();";

        /// <summary>页面转发来的 F11 消息标识</summary>
        private const string HotkeyMessage = "loong:f11";

        /// <summary>页面消息的最短间隔（毫秒）：防止一次按键被多帧/重复投递处理两次</summary>
        private const int PageHotkeyCooldownMs = 250;

        private static DateTime _lastPageHotkey = DateTime.MinValue;

        // ---------- 每个窗口一份的界面状态 ----------
        private class UiState
        {
            public Form Form;
            public MenuStrip Menu;
            public ToolStrip Toolbar;
            public TabControl Tabs;
            // ---- 自绘标题栏（玻璃外壳）----
            // 窗口恒为 FormBorderStyle.None，系统标题栏不存在了；真全屏时把这条
            // 自绘标题栏也藏掉才是"真"全屏（轻量全屏保留它，见文件顶部说明）。
            public TitleBar TitleBar;
            public bool Active;                 // 该窗口是否处于 F11 轻量全屏
            public bool SavedMenuVisible;
            public bool SavedToolbarVisible;
            public FormWindowState SavedWindowState;

            // ---- 自绘外壳（Vista 玻璃）----
            // 菜单、工具栏、标签头合并成一条 GlassChrome，全屏时作为整体隐藏。
            public GlassChrome Chrome;
            public bool SavedChromeVisible;
        }

        private static readonly List<UiState> _states = new List<UiState>();

        // ---------- 网页真全屏：保存/恢复 ----------
        private static UiState _fsState;            // 处于真全屏的那个窗口
        private static FormWindowState _savedWindowState;
        private static Rectangle _savedBounds;
        private static FormBorderStyle _savedBorderStyle;
        private static bool _savedTopMost;
        private static bool _savedMenuVisible;
        private static bool _savedToolbarVisible;
        private static bool _savedTabsVisible;
        private static bool _savedChromeVisible;
        private static bool _savedTitleBarVisible;

        // ---------- 当前全屏控件 ----------
        private static WebView2 _view;
        private static Control _originalParent;     // 原来的 TabPage
        private static DockStyle _originalDock;
        private static int _originalChildIndex;     // 在父控件中的位置

        /// <summary>初始化（自绘外壳版本）：标题栏 + 菜单/工具栏/标签头合并为一条 chrome</summary>
        public static void Init(Form form, TitleBar titleBar, GlassChrome chrome, TabControl tabs)
        {
            if (form == null) return;
            RemoveState(form);                      // 防重复登记

            var st = new UiState();
            st.Form = form;
            st.TitleBar = titleBar;
            st.Chrome = chrome;
            st.Tabs = tabs;
            _states.Add(st);

            form.KeyPreview = true;
            form.KeyDown += OnKeyDown;
            form.FormClosed += delegate { RemoveState(form); };
        }

        /// <summary>初始化（原生控件版本，保留给测试与旧调用点）</summary>
        public static void Init(Form form, MenuStrip menu, ToolStrip toolbar, TabControl tabs)
        {
            if (form == null) return;
            RemoveState(form);                      // 防重复登记

            var st = new UiState();
            st.Form = form;
            st.Menu = menu;
            st.Toolbar = toolbar;
            st.Tabs = tabs;
            _states.Add(st);

            form.KeyPreview = true;
            form.KeyDown += OnKeyDown;
            form.FormClosed += delegate { RemoveState(form); };
        }

        // ---------- 外壳可见性（两种外壳形态统一入口） ----------

        /// <summary>取当前外壳是否可见（Chrome 优先，其次旧 Menu/Toolbar）</summary>
        private static bool GetChromeVisible(UiState st)
        {
            if (st == null) return false;
            if (st.Chrome != null) return st.Chrome.Visible;
            bool m = st.Menu == null || st.Menu.Visible;
            bool t = st.Toolbar == null || st.Toolbar.Visible;
            return m && t;
        }

        /// <summary>设置当前外壳是否可见</summary>
        private static void SetChromeVisible(UiState st, bool visible)
        {
            if (st == null) return;
            if (st.Chrome != null)
            {
                st.Chrome.Visible = visible;
                return;
            }
            if (st.Menu != null) st.Menu.Visible = visible;
            if (st.Toolbar != null) st.Toolbar.Visible = visible;
        }

        /// <summary>取标签栏是否可见</summary>
        private static bool GetTabsVisible(UiState st)
        {
            return st != null && st.Tabs != null && st.Tabs.Visible;
        }

        /// <summary>设置标签栏是否可见</summary>
        private static void SetTabsVisible(UiState st, bool visible)
        {
            if (st != null && st.Tabs != null) st.Tabs.Visible = visible;
        }

        /// <summary>指定窗口当前是否处于 F11 轻量全屏</summary>
        public static bool IsUiFullscreen(Form form)
        {
            UiState st = FindState(form);
            return st != null && st.Active;
        }

        /// <summary>切换指定窗口的 F11 轻量全屏（F11 键与页面转发共用的入口，测试也走它）</summary>
        public static void Toggle(Form form)
        {
            ToggleF11(FindState(form));
        }

        /// <summary>为 WebView2 绑定全屏事件与 F11 转发（每个新标签页调用一次）</summary>
        public static async void BindWebView(WebView2 view)
        {
            if (view == null || view.CoreWebView2 == null) return;
            try
            {
                view.CoreWebView2.ContainsFullScreenElementChanged += OnFullScreenChanged;
                view.CoreWebView2.WebMessageReceived += OnWebMessage;
                // 注入 F11 转发脚本：对之后创建的每个文档（含每次导航、子框架）生效
                await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HotkeyScript);
            }
            catch (Exception) { }
        }

        // ============================================================
        //  界面状态查找
        // ============================================================

        private static UiState FindState(Control c)
        {
            if (c == null) return null;
            Form f = c as Form;
            if (f == null) f = c.FindForm();
            return FindState(f);
        }

        private static UiState FindState(Form f)
        {
            if (f == null) return null;
            for (int i = 0; i < _states.Count; i++)
                if (_states[i].Form == f) return _states[i];
            return null;
        }

        private static void RemoveState(Form f)
        {
            for (int i = _states.Count - 1; i >= 0; i--)
                if (_states[i].Form == f) _states.RemoveAt(i);
        }

        /// <summary>通过 CoreWebView2 实例找到宿主 WebView2 控件（遍历所有已登记窗口）</summary>
        private static WebView2 FindViewByCore(CoreWebView2 core)
        {
            for (int s = 0; s < _states.Count; s++)
            {
                UiState st = _states[s];
                if (st.Form == null) continue;

                if (st.Tabs != null)
                {
                    foreach (TabPage page in st.Tabs.TabPages)
                    {
                        foreach (Control c in page.Controls)
                        {
                            var wv = c as WebView2;
                            if (wv != null && wv.CoreWebView2 == core) return wv;
                        }
                    }
                }
                // 全屏时 WebView2 已挂到 Form 上
                foreach (Control c in st.Form.Controls)
                {
                    var wv = c as WebView2;
                    if (wv != null && wv.CoreWebView2 == core) return wv;
                }
            }
            return null;
        }

        // ============================================================
        //  1) 网页真全屏（Fullscreen API）
        // ============================================================

        private static void OnFullScreenChanged(object sender, object e)
        {
            var core = sender as CoreWebView2;
            if (core == null) return;

            WebView2 hit = FindViewByCore(core);
            if (hit == null) return;

            if (core.ContainsFullScreenElement && !IsFullscreen)
                EnterFullscreen(hit);
            else if (!core.ContainsFullScreenElement && IsFullscreen)
                ExitFullscreen();
        }

        /// <summary>进入网页真全屏</summary>
        private static void EnterFullscreen(WebView2 view)
        {
            if (IsFullscreen || view == null) return;

            UiState st = FindState(view);
            if (st == null || st.Form == null) return;      // 未登记的窗口不处理，避免操作错窗口
            Form form = st.Form;

            _fsState = st;
            _view = view;

            // ---- 1) 记录原始状态 ----
            _originalParent = view.Parent;
            _originalDock = view.Dock;
            _originalChildIndex = _originalParent != null ? _originalParent.Controls.IndexOf(view) : -1;

            _savedBorderStyle = form.FormBorderStyle;
            _savedWindowState = form.WindowState;
            _savedBounds = form.Bounds;
            _savedTopMost = form.TopMost;
            // 轻量全屏期间菜单/工具栏本来就是隐藏的 → 这里保存的也是“隐藏”，
            // 退出真全屏后轻量全屏的状态得以保留
            _savedChromeVisible = GetChromeVisible(st);
            _savedTitleBarVisible = st.TitleBar != null && st.TitleBar.Visible;
            _savedMenuVisible = st.Menu != null && st.Menu.Visible;
            _savedToolbarVisible = st.Toolbar != null && st.Toolbar.Visible;
            _savedTabsVisible = GetTabsVisible(st);

            // ---- 2) 从原父控件摘除 WebView2 ----
            if (_originalParent != null) _originalParent.Controls.Remove(view);

            // ---- 3) 隐藏浏览器 UI（含自绘标题栏：真全屏连它一起藏掉）----
            if (st.TitleBar != null) st.TitleBar.Visible = false;
            SetChromeVisible(st, false);
            SetTabsVisible(st, false);

            // ---- 4) 把 WebView2 直接挂到 Form 上 ----
            view.Dock = DockStyle.Fill;
            form.Controls.Add(view);
            view.BringToFront();

            // ---- 5) 窗口切无边框铺满屏幕 ----
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Normal;       // 先 Normal 才能设 Bounds
            form.Bounds = Screen.FromControl(form).Bounds;
            form.TopMost = true;

            IsFullscreen = true;
        }

        /// <summary>退出网页真全屏（恢复窗口、把 WebView2 放回原标签页、还原界面）</summary>
        public static void ExitFullscreen()
        {
            if (!IsFullscreen || _view == null || _fsState == null) return;
            UiState st = _fsState;
            Form form = st.Form;
            if (form == null || form.IsDisposed)
            {
                IsFullscreen = false;
                _fsState = null;
                _view = null;
                return;
            }

            // ---- 1) 窗口恢复 ----
            form.FormBorderStyle = _savedBorderStyle;
            form.TopMost = _savedTopMost;
            form.Bounds = _savedBounds;
            form.WindowState = _savedWindowState;

            // ---- 2) 把 WebView2 从 Form 上摘除 ----
            form.Controls.Remove(_view);

            // ---- 3) 放回原来的 TabPage ----
            if (_originalParent != null && !_originalParent.IsDisposed)
            {
                _originalParent.Controls.Add(_view);
                // 尽量恢复原来的位置（Controls.Add 会放到末尾）
                int idx = Math.Min(_originalChildIndex, _originalParent.Controls.Count - 1);
                if (idx >= 0)
                    _originalParent.Controls.SetChildIndex(_view, idx);
            }
            _view.Dock = _originalDock;

            // ---- 4) 恢复浏览器 UI ----
            if (st.TitleBar != null) st.TitleBar.Visible = _savedTitleBarVisible;
            if (st.Chrome != null) st.Chrome.Visible = _savedChromeVisible;
            if (st.Menu != null) st.Menu.Visible = _savedMenuVisible;
            if (st.Toolbar != null) st.Toolbar.Visible = _savedToolbarVisible;
            SetTabsVisible(st, _savedTabsVisible);

            IsFullscreen = false;
            _fsState = null;
            _view = null;
            _originalParent = null;
        }

        /// <summary>通知网页退出全屏（网页端触发 fullscreenchange → ContainsFullScreenElement 变 false
        /// → 自动回调 ExitFullscreen）；网页没响应时直接恢复界面兜底</summary>
        private static async void ExitWebFullscreen()
        {
            WebView2 view = _view;
            if (view == null || view.CoreWebView2 == null)
            {
                ExitFullscreen();
                return;
            }
            try
            {
                await view.CoreWebView2.ExecuteScriptAsync(
                    "if (document.fullscreenElement) document.exitFullscreen();");
            }
            catch (Exception) { }
            if (IsFullscreen) ExitFullscreen();
        }

        // ============================================================
        //  2) F11 轻量全屏（隐藏菜单 + 工具栏，窗口最大化）
        // ============================================================

        private static void ToggleF11(UiState st)
        {
            if (st == null || st.Form == null || st.Form.IsDisposed) return;

            // 网页真全屏优先：这一下先退真全屏，再按才是轻量全屏
            if (IsFullscreen && _fsState == st)
            {
                ExitWebFullscreen();
                return;
            }

            if (st.Active) ExitUiFullscreen(st);
            else EnterUiFullscreen(st);
        }

        private static void EnterUiFullscreen(UiState st)
        {
            if (st.Active) return;

            st.SavedChromeVisible = GetChromeVisible(st);
            st.SavedMenuVisible = st.Menu != null && st.Menu.Visible;
            st.SavedToolbarVisible = st.Toolbar != null && st.Toolbar.Visible;
            // 最小化状态下按 F11 没意义，按 Normal 记，退出时不把窗口还原成最小化
            st.SavedWindowState = st.Form.WindowState == FormWindowState.Minimized
                ? FormWindowState.Normal
                : st.Form.WindowState;

            SetChromeVisible(st, false);
            if (st.Form.WindowState != FormWindowState.Maximized)
                st.Form.WindowState = FormWindowState.Maximized;

            st.Active = true;
        }

        private static void ExitUiFullscreen(UiState st)
        {
            if (!st.Active) return;

            if (st.Chrome != null) st.Chrome.Visible = st.SavedChromeVisible;
            if (st.Menu != null) st.Menu.Visible = st.SavedMenuVisible;
            if (st.Toolbar != null) st.Toolbar.Visible = st.SavedToolbarVisible;

            // 真全屏期间窗口状态归真全屏管，别在这里抢着改
            if (!(IsFullscreen && _fsState == st))
                st.Form.WindowState = st.SavedWindowState;

            st.Active = false;
        }

        // ============================================================
        //  输入入口
        // ============================================================

        /// <summary>宿主侧：焦点在地址栏等控件时的 F11 / Esc</summary>
        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            UiState st = FindState(sender as Control);
            if (st == null) return;

            if (e.KeyCode == Keys.Escape && IsFullscreen && _fsState == st)
            {
                // 让 WebView2 退出网页级全屏，随后由事件链回调 ExitFullscreen
                ExitWebFullscreen();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F11)
            {
                ToggleF11(st);
                e.Handled = true;
            }
        }

        /// <summary>网页侧：页面脚本转发来的 F11（焦点在网页里时的唯一入口）</summary>
        private static void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = null;
            try { msg = e.TryGetWebMessageAsString(); } catch (Exception) { return; }
            if (msg != HotkeyMessage) return;

            DateTime now = DateTime.Now;
            if ((now - _lastPageHotkey).TotalMilliseconds < PageHotkeyCooldownMs) return;
            _lastPageHotkey = now;

            var core = sender as CoreWebView2;
            if (core == null) return;
            WebView2 view = FindViewByCore(core);
            if (view == null) return;
            ToggleF11(FindState(view));
        }
    }
}
