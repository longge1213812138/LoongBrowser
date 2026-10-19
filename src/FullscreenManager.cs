// FullscreenManager.cs —— 两套全屏：视频真全屏（Video） + 网页全屏（Page）
//
// 用户 2026-10 的需求：视频全屏有边框（远处那圈 6px 玻璃），要真全屏；
// 并且必须与"网页全屏"区分开，两套各自独立、互不顶替。
//
// ============================ 两套的分工 ============================
//
//  A) 视频真全屏（FullscreenKind.Video）
//     触发：网页调用标准 Fullscreen API（B 站播放器右下角「全屏」按钮、
//           自定义播放器的 fullscreen 按钮、<video>.requestFullscreen()）。
//     表现：整个屏幕归视频 ——
//           · WebView2 从 TabPage 摘出来直接挂到 Form 上（脱离标签容器）
//           · 自绘标题栏、菜单、工具栏、地址栏、标签头全部隐藏
//           · Form.Padding 归零（**这是过去那圈边框的根因**：窗口留了一圈
//             6px 的缩放热区当 Padding，不归零则 WebView2 被挤在里面，
//             四周露出 6px 玻璃 + 白高光，看起来就是"视频全屏有边框"）
//           · 窗口无边框、覆盖整块屏幕（含任务栏）、置顶
//     退出：Esc / F11 / 网页自己退出 Fullscreen API，一切原样还原。
//
//  B) 网页全屏（FullscreenKind.Page）
//     触发：宿主入口（F11 或「查看 → 网页全屏」菜单）。
//     表现：只让"浏览器外壳"退场，窗口仍然是窗口 ——
//           · 菜单 / 工具栏 / 地址栏 / 标签头（GlassChrome 整条）隐藏
//           · WebView2 **留在原标签页里**（不摘、不搬），TabControl 继续当容器
//           · 自绘标题栏保留（还能拖动、最大化、关闭）
//           · Form.Padding 归零，让网页铺满整个客户区
//           · 窗口最大化到工作区，任务栏可见、不置顶
//     退出：再按 F11 / 菜单「退出全屏」。
//
//  两者叠加：网页全屏中进视频全屏 → 视频全屏按当时的可见性（外壳已隐藏）
//  保存；退出视频全屏后仍是网页全屏。这一点有端到端测试锁着。
//
//  为什么 B 不摘 WebView2：网页全屏要的只是"外壳让位"，网页本来就填满
//  TabPage；TabControl 的标签头早已被 TabStrip.HideHeader 压成 0，视觉上的
//  标签条是 GlassChrome 画的，所以藏掉 Chrome 就等于藏掉标签栏 —— 不必搬控件。
//  站点自己的「网页全屏」（B 站的 CSS 变换）在此之上叠加：播放器撑满 TabPage
//  = 撑满整个窗口客户区 = 铺满整屏（除标题栏与任务栏），两者天然协同。
//
// F11 的两条入口（互斥，同一次按键只会走其中一条）：
//   · 焦点在地址栏等宿主控件 → WinForms KeyDown（Init 里开了 KeyPreview）
//   · 焦点在网页里 → 由 AddScriptToExecuteOnDocumentCreatedAsync 注入的页面脚本监听 keydown，
//     再 window.chrome.webview.postMessage 回宿主。
//     背景：网页有焦点时，按键消息的接收方是 WebView2 内核**进程**里的窗口，不会进宿主
//     的消息队列；不引入系统级钩子，页面内转发就是唯一的通道 —— 与新标签页点击桥同一套机制。
//
// 多窗口：每个 Form 一份 UiState，F11 只作用于按键来源的那个窗口；关窗时自动清理。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    /// <summary>全屏形态（两套，互不顶替）</summary>
    public enum FullscreenKind
    {
        /// <summary>不在任何全屏里</summary>
        None = 0,
        /// <summary>视频真全屏：网页 Fullscreen API 触发，无边框铺满整屏（含任务栏）</summary>
        Video = 1,
        /// <summary>网页全屏：宿主入口触发，浏览器外壳让位但窗口与标题栏还在</summary>
        Page = 2
    }

    /// <summary>全屏管理器</summary>
    public static class FullscreenManager
    {
        /// <summary>视频真全屏（Fullscreen API）是否生效</summary>
        public static bool IsFullscreen { get { return _videoActive; } }

        /// <summary>
        /// 当前全屏形态。由各窗口的状态推导，不单独存一份 ——
        /// 两套全屏可叠加，存一份就一定会漂移（例如从视频全屏退出后，
        /// 网页全屏其实还在，存的那份却会说"没了"）。
        /// 优先级：视频真全屏 > 网页全屏 > 无。
        /// </summary>
        public static FullscreenKind Kind
        {
            get
            {
                if (_videoActive) return FullscreenKind.Video;
                for (int i = 0; i < _states.Count; i++)
                    if (_states[i].Active) return FullscreenKind.Page;
                return FullscreenKind.None;
            }
        }

        /// <summary>视频真全屏是否生效（内部标记，对外只读 Kind / IsFullscreen）</summary>
        private static bool _videoActive;

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
            // 窗口恒为 FormBorderStyle.None，系统标题栏不存在了；视频真全屏时把这条
            // 自绘标题栏也藏掉才是"真"全屏（网页全屏保留它）。
            public TitleBar TitleBar;

            /// <summary>该窗口是否处于网页全屏</summary>
            public bool Active;
            public bool SavedChromeVisible;
            public bool SavedMenuVisible;
            public bool SavedToolbarVisible;
            public bool SavedTabsVisible;
            public FormWindowState SavedWindowState;
            public Padding SavedPadding;

            // ---- 自绘外壳（Vista 玻璃）----
            // 菜单、工具栏、地址栏、标签头合并成一条 GlassChrome，全屏时作为整体隐藏。
            public GlassChrome Chrome;
        }

        private static readonly List<UiState> _states = new List<UiState>();

        // ---------- 视频真全屏：保存/恢复 ----------
        private static UiState _fsState;            // 处于视频全屏的那个窗口
        private static FormWindowState _savedWindowState;
        private static Rectangle _savedBounds;
        private static FormBorderStyle _savedBorderStyle;
        private static Padding _savedPadding;
        private static bool _savedTopMost;
        private static bool _savedMenuVisible;
        private static bool _savedToolbarVisible;
        private static bool _savedTabsVisible;
        private static bool _savedChromeVisible;
        private static bool _savedTitleBarVisible;

        // ---------- 视频真全屏的当前控件 ----------
        private static WebView2 _view;

        // ---------- 把网页"抬到窗口上"的共用机制 ----------
        //
        // 两套全屏都必须把 WebView2 从 TabPage 里摘出来挂到 Form 上，否则边上有东西：
        //   · 视频全屏时窗口 Padding 不归零 → 四周 6px 玻璃（用户报的那圈边框）
        //   · 网页全屏时 TabControl 自己有 4px 边框 + 标签头残留高度
        //     （实测 webview={4,35,2552,1353} vs 工作区 2560x1392）
        //
        // 两套可以叠加，所以摘/放要用**引用计数**：先进网页全屏、再进视频全屏时，
        // 第二次不能再"记录原位置"（那时原位置已经是 Form 了），退出时也只该由
        // 最后一次退出负责放回。否则控件会被放到 Form 上再也回不去。
        private static WebView2 _hostedView;          // 当前被抬到 Form 上的那个
        private static Control _hostOriginalParent;   // 它真正的原父控件（TabPage）
        private static DockStyle _hostOriginalDock;
        private static int _hostOriginalIndex;
        private static int _hostRefCount;             // 有多少套全屏要求它被抬起来

        /// <summary>把网页抬到窗口上（引用计数；第一次才真的搬）</summary>
        private static void HostView(Form form, WebView2 view)
        {
            if (form == null || view == null) return;

            // 已有另一个网页在窗口上（极端情况：两套全屏分属不同标签）——
            // 先把它放回去，免得它的原位置被覆盖后再也回不去。
            if (_hostRefCount > 0 && _hostedView != null && _hostedView != view)
                UnhostView(_hostedView.FindForm());

            if (_hostRefCount == 0 || _hostedView != view)
            {
                // 第一次：记住真实位置
                if (_hostedView != view)
                {
                    _hostedView = view;
                    _hostOriginalParent = view.Parent;
                    _hostOriginalDock = view.Dock;
                    _hostOriginalIndex = view.Parent != null ? view.Parent.Controls.IndexOf(view) : -1;
                }
                if (_hostOriginalParent != null) _hostOriginalParent.Controls.Remove(view);

                view.Dock = DockStyle.Fill;
                form.Controls.Add(view);      // 后加 → docking 布局里最后处理，被 Dock.Top 的标题栏正确让位
                view.BringToFront();
            }
            _hostRefCount++;
        }

        /// <summary>放回原处（引用计数归零才真的搬回去）</summary>
        private static void UnhostView(Form form)
        {
            if (_hostRefCount <= 0) return;
            _hostRefCount--;
            if (_hostRefCount > 0) return;

            WebView2 view = _hostedView;
            Control parent = _hostOriginalParent;
            _hostedView = null;
            _hostOriginalParent = null;

            if (view == null || view.IsDisposed) return;
            try
            {
                if (form != null && !form.IsDisposed) form.Controls.Remove(view);
                if (parent != null && !parent.IsDisposed)
                {
                    parent.Controls.Add(view);
                    int idx = Math.Min(_hostOriginalIndex, parent.Controls.Count - 1);
                    if (idx >= 0) parent.Controls.SetChildIndex(view, idx);
                }
                view.Dock = _hostOriginalDock;
            }
            catch (Exception) { }
        }

        /// <summary>附件：这两套全屏各自要求"网页被抬起来"</summary>
        private static void HostForVideo(Form form, WebView2 view) { HostView(form, view); }
        private static void HostForPage(Form form, WebView2 view) { HostView(form, view); }

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

        /// <summary>指定窗口当前是否处于网页全屏</summary>
        public static bool IsPageFullscreen(Form form)
        {
            UiState st = FindState(form);
            return st != null && st.Active;
        }

        /// <summary>兼容旧名：等同于 <see cref="IsPageFullscreen"/></summary>
        public static bool IsUiFullscreen(Form form)
        {
            return IsPageFullscreen(form);
        }

        /// <summary>指定窗口是否处于任意一种全屏（视频真全屏 / 网页全屏）</summary>
        public static bool IsAnyFullscreen(Form form)
        {
            if (IsPageFullscreen(form)) return true;
            return IsFullscreen && _fsState != null && _fsState.Form == form;
        }

        /// <summary>切换指定窗口的网页全屏（F11 与页面转发共用的入口，测试也走它）</summary>
        public static void Toggle(Form form)
        {
            TogglePage(FindState(form));
        }

        /// <summary>退出指定窗口的任意全屏（菜单「退出全屏」与 Esc 用）</summary>
        public static void ExitAll(Form form)
        {
            if (IsFullscreen && _fsState != null && _fsState.Form == form)
            {
                ExitWebFullscreen();
                return;
            }
            UiState st = FindState(form);
            if (st != null && st.Active) ExitPageFullscreen(st);
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
                // 视频真全屏时 WebView2 已挂到 Form 上
                foreach (Control c in st.Form.Controls)
                {
                    var wv = c as WebView2;
                    if (wv != null && wv.CoreWebView2 == core) return wv;
                }
            }
            return null;
        }

        // ============================================================
        //  A) 视频真全屏（Fullscreen API）
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

        /// <summary>
        /// 进入视频真全屏。
        /// 关键一步是 <c>form.Padding = Padding.Empty</c>：窗口给无边框留了一圈
        /// 6px 缩放热区当 Padding，不归零的话 WebView2 被 Dock 挤在里面，
        /// 四周露出 6px 玻璃 + 白色高光 —— 用户看到的就是"视频全屏有边框"。
        /// </summary>
        private static void EnterFullscreen(WebView2 view)
        {
            if (IsFullscreen || view == null) return;

            UiState st = FindState(view);
            if (st == null || st.Form == null) return;      // 未登记的窗口不处理，避免操作错窗口
            Form form = st.Form;

            _fsState = st;
            _view = view;

            // ---- 1) 记录原始状态 ----
            _savedBorderStyle = form.FormBorderStyle;
            _savedWindowState = form.WindowState;
            _savedBounds = form.Bounds;
            _savedTopMost = form.TopMost;
            _savedPadding = form.Padding;
            // 网页全屏期间外壳本来就是隐藏的 → 这里保存的也是"隐藏"，
            // 退出视频全屏后网页全屏的状态得以保留
            _savedChromeVisible = GetChromeVisible(st);
            _savedTitleBarVisible = st.TitleBar != null && st.TitleBar.Visible;
            _savedMenuVisible = st.Menu != null && st.Menu.Visible;
            _savedToolbarVisible = st.Toolbar != null && st.Toolbar.Visible;
            _savedTabsVisible = GetTabsVisible(st);

            // ---- 2) 内边距归零：先做，否则 Dock.Fill 会把 WebView2 挤进 6px 里 ----
            form.Padding = Padding.Empty;

            // ---- 3) 隐藏浏览器 UI（含自绘标题栏：真全屏连它一起藏掉）----
            if (st.TitleBar != null) st.TitleBar.Visible = false;
            SetChromeVisible(st, false);
            SetTabsVisible(st, false);

            // ---- 4) 把 WebView2 直接挂到 Form 上（引用计数，见 HostView）----
            HostForVideo(form, view);

            // ---- 5) 窗口切无边框铺满整块屏幕（含任务栏）----
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Normal;       // 先 Normal 才能设 Bounds
            form.Bounds = Screen.FromControl(form).Bounds;
            form.TopMost = true;

            _videoActive = true;
        }

        /// <summary>退出视频真全屏（恢复窗口、把 WebView2 放回原标签页、还原界面）</summary>
        public static void ExitFullscreen()
        {
            if (!_videoActive || _view == null || _fsState == null)
            {
                _videoActive = false;
                _fsState = null;
                _view = null;
                return;
            }
            UiState st = _fsState;
            Form form = st.Form;
            if (form == null || form.IsDisposed)
            {
                _videoActive = false;
                _fsState = null;
                _view = null;
                return;
            }

            // ---- 1) 窗口恢复 ----
            form.FormBorderStyle = _savedBorderStyle;
            form.TopMost = _savedTopMost;
            form.Padding = _savedPadding;
            form.Bounds = _savedBounds;
            form.WindowState = _savedWindowState;

            // ---- 2) 放回原处（引用计数：网页全屏还在时它继续留在窗口上）----
            UnhostView(form);

            // ---- 3) 恢复浏览器 UI ----
            if (st.TitleBar != null) st.TitleBar.Visible = _savedTitleBarVisible;
            if (st.Chrome != null) st.Chrome.Visible = _savedChromeVisible;
            if (st.Menu != null) st.Menu.Visible = _savedMenuVisible;
            if (st.Toolbar != null) st.Toolbar.Visible = _savedToolbarVisible;
            SetTabsVisible(st, _savedTabsVisible);

            _videoActive = false;
            _fsState = null;
            _view = null;
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
        //  B) 网页全屏（宿主入口：F11 / 查看菜单）
        // ============================================================

        private static void TogglePage(UiState st)
        {
            if (st == null || st.Form == null || st.Form.IsDisposed) return;

            // 视频真全屏优先：这一下先退视频全屏，再按才是网页全屏
            if (IsFullscreen && _fsState == st)
            {
                ExitWebFullscreen();
                return;
            }

            if (st.Active) ExitPageFullscreen(st);
            else EnterPageFullscreen(st);
        }

        /// <summary>
        /// 进入网页全屏：外壳让位，窗口还是窗口。
        ///
        /// 这里必须把 WebView2 抬到 Form 上，否则边上还有东西 —— 实测
        /// <c>webview={4,35,2552,1353}</c>，而工作区是 2560x1392：TabControl 自带
        /// 4px 边框，标签头也还占着高度。只藏 Chrome 是不够的。
        /// 与视频全屏共用引用计数（<see cref="HostView"/>），两套叠加时不会互相踩。
        /// </summary>
        private static void EnterPageFullscreen(UiState st)
        {
            if (st.Active) return;

            st.SavedChromeVisible = GetChromeVisible(st);
            st.SavedMenuVisible = st.Menu != null && st.Menu.Visible;
            st.SavedToolbarVisible = st.Toolbar != null && st.Toolbar.Visible;
            st.SavedTabsVisible = GetTabsVisible(st);
            st.SavedPadding = st.Form.Padding;
            // 最小化状态下按 F11 没意义，按 Normal 记，退出时不把窗口还原成最小化
            st.SavedWindowState = st.Form.WindowState == FormWindowState.Minimized
                ? FormWindowState.Normal
                : st.Form.WindowState;

            SetChromeVisible(st, false);
            // 内边距归零：网页铺满整个客户区（四周不再留 6px 玻璃边）
            if (!IsFullscreen) st.Form.Padding = Padding.Empty;

            // 把网页抬到窗口上（叠加时由引用计数决定要不要真的搬）
            WebView2 active = ActiveViewOf(st);
            if (active != null) HostForPage(st.Form, active);

            // 最大化到工作区：任务栏保持可见，这是与视频真全屏的关键区别
            if (!IsFullscreen && st.Form.WindowState != FormWindowState.Maximized)
                st.Form.WindowState = FormWindowState.Maximized;

            st.Active = true;
        }

        /// <summary>某个窗口当前选中标签的 WebView2</summary>
        private static WebView2 ActiveViewOf(UiState st)
        {
            if (st == null || st.Tabs == null || st.Tabs.TabPages.Count == 0) return null;
            TabPage page = st.Tabs.SelectedTab;
            if (page == null) return null;
            foreach (Control c in page.Controls)
            {
                var wv = c as WebView2;
                if (wv != null) return wv;
            }
            return null;
        }

        private static void ExitPageFullscreen(UiState st)
        {
            if (!st.Active) return;

            if (st.Chrome != null) st.Chrome.Visible = st.SavedChromeVisible;
            if (st.Menu != null) st.Menu.Visible = st.SavedMenuVisible;
            if (st.Toolbar != null) st.Toolbar.Visible = st.SavedToolbarVisible;
            SetTabsVisible(st, st.SavedTabsVisible);

            // 视频全屏期间窗口状态归视频全屏管，别在这里抢着改
            if (!(IsFullscreen && _fsState == st))
            {
                UnhostView(st.Form);
                st.Form.Padding = st.SavedPadding;
                st.Form.WindowState = st.SavedWindowState;
            }

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
                TogglePage(st);
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
            TogglePage(FindState(view));
        }
    }
}
