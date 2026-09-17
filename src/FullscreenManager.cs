// FullscreenManager.cs —— 视频全屏管理
//
// 核心方案：
//   进入全屏时，把 WebView2 从 TabPage 中摘除，直接挂到 Form 上；
//   同时隐藏 Menu / Toolbar / TabControl，窗口切无边框铺满屏幕。
//   退出全屏时，把 WebView2 放回原来的 TabPage，恢复所有界面。
//
// 事件链：
//   网页调用 Element.requestFullscreen()
//   → WebView2 触发 ContainsFullScreenElementChanged
//   → 我们 reparent WebView2 到 Form 并全屏显示
//
// bilibili 说明：
//   bilibili 播放器的「全屏」按钮走标准 Fullscreen API，会触发上述事件链。
//   「网页全屏」按钮是 bilibili 自己的 CSS 变换，不走 Fullscreen API，
//   不在本次处理范围内（网页全屏只是让播放器撑满网页视口，不影响浏览器窗口）。
//
// 已知限制：
//   - 全屏请求必须由用户手势触发（点击等），程序化调用 requestFullscreen() 会被拦截
//   - 退出全屏时 WebView2 内部的 fullscreenElement 会同步退出（浏览器标准行为）
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    /// <summary>全屏管理器</summary>
    public static class FullscreenManager
    {
        public static bool IsFullscreen { get; private set; }

        // ---------- 保存/恢复用 ----------
        private static FormWindowState _savedWindowState;
        private static Rectangle _savedBounds;
        private static FormBorderStyle _savedBorderStyle;
        private static bool _savedTopMost;
        private static bool _savedMenuVisible;
        private static bool _savedToolbarVisible;
        private static bool _savedTabsVisible;

        // ---------- 当前全屏控件 ----------
        private static WebView2 _view;
        private static Control _originalParent;   // 原来的 TabPage
        private static DockStyle _originalDock;
        private static int _originalChildIndex;    // 在父控件中的位置

        // ---------- 外部引用 ----------
        private static MainForm _mainForm;
        private static MenuStrip _menu;
        private static ToolStrip _toolbar;
        private static TabControl _tabs;

        /// <summary>初始化（在 MainForm 构造函数中调用一次）</summary>
        public static void Init(MainForm form, MenuStrip menu, ToolStrip toolbar, TabControl tabs)
        {
            _mainForm = form;
            _menu = menu;
            _toolbar = toolbar;
            _tabs = tabs;
            _mainForm.KeyPreview = true;
            _mainForm.KeyDown += OnKeyDown;
        }

        /// <summary>为 WebView2 绑定全屏事件（每个新标签页调用一次）</summary>
        public static void BindWebView(WebView2 view)
        {
            if (view == null || view.CoreWebView2 == null) return;
            view.CoreWebView2.ContainsFullScreenElementChanged += OnFullScreenChanged;
        }

        // ============================================================
        //  核心逻辑
        // ============================================================

        private static void OnFullScreenChanged(object sender, object e)
        {
            var core = sender as Microsoft.Web.WebView2.Core.CoreWebView2;
            if (core == null) return;

            // 找到触发事件的 WebView2 控件（通过遍历 TabManager 的标签列表太慢，
            // 所以直接在所有打开的 WebView2 中找 CoreWebView2 匹配的那个）
            WebView2 hit = FindViewByCore(core);
            if (hit == null) return;

            if (core.ContainsFullScreenElement && !IsFullscreen)
                EnterFullscreen(hit);
            else if (!core.ContainsFullScreenElement && IsFullscreen)
                ExitFullscreen();
        }

        /// <summary>通过 CoreWebView2 实例找到宿主 WebView2 控件</summary>
        private static WebView2 FindViewByCore(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            // 在 TabControl 的所有 TabPage 中查找
            if (_tabs != null)
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    foreach (Control c in page.Controls)
                    {
                        var wv = c as WebView2;
                        if (wv != null && wv.CoreWebView2 == core) return wv;
                    }
                }
            }
            // 全屏时 WebView2 已挂到 Form 上，从 Form 直接查
            if (_mainForm != null)
            {
                foreach (Control c in _mainForm.Controls)
                {
                    var wv = c as WebView2;
                    if (wv != null && wv.CoreWebView2 == core) return wv;
                }
            }
            return null;
        }

        /// <summary>进入全屏</summary>
        private static void EnterFullscreen(WebView2 view)
        {
            if (IsFullscreen || view == null || _mainForm == null) return;

            _view = view;

            // ---- 1) 记录原始状态 ----
            _originalParent = view.Parent;
            _originalDock = view.Dock;
            _originalChildIndex = _originalParent.Controls.IndexOf(view);

            _savedBorderStyle = _mainForm.FormBorderStyle;
            _savedWindowState = _mainForm.WindowState;
            _savedBounds = _mainForm.Bounds;
            _savedTopMost = _mainForm.TopMost;
            _savedMenuVisible = _menu != null && _menu.Visible;
            _savedToolbarVisible = _toolbar != null && _toolbar.Visible;
            _savedTabsVisible = _tabs != null && _tabs.Visible;

            // ---- 2) 从原父控件摘除 WebView2 ----
            _originalParent.Controls.Remove(view);

            // ---- 3) 隐藏浏览器 UI ----
            if (_menu != null) _menu.Visible = false;
            if (_toolbar != null) _toolbar.Visible = false;
            if (_tabs != null) _tabs.Visible = false;

            // ---- 4) 把 WebView2 直接挂到 Form ----
            view.Dock = DockStyle.Fill;
            _mainForm.Controls.Add(view);
            view.BringToFront();

            // ---- 5) 窗口切无边框铺满屏幕 ----
            _mainForm.FormBorderStyle = FormBorderStyle.None;
            _mainForm.WindowState = FormWindowState.Normal;          // 先 Normal 才能设 Bounds
            _mainForm.Bounds = Screen.FromControl(_mainForm).Bounds;
            _mainForm.TopMost = true;

            IsFullscreen = true;
        }

        /// <summary>退出全屏</summary>
        public static void ExitFullscreen()
        {
            if (!IsFullscreen || _view == null || _mainForm == null) return;

            // ---- 1) 窗口恢复 ----
            _mainForm.FormBorderStyle = _savedBorderStyle;
            _mainForm.TopMost = _savedTopMost;
            _mainForm.Bounds = _savedBounds;
            _mainForm.WindowState = _savedWindowState;

            // ---- 2) 把 WebView2 从 Form 上摘除 ----
            _mainForm.Controls.Remove(_view);

            // ---- 3) 放回原来的 TabPage ----
            if (_originalParent != null)
            {
                _originalParent.Controls.Add(_view);
                // 尽量恢复原来的位置（Controls.Add 会放到末尾）
                int idx = Math.Min(_originalChildIndex, _originalParent.Controls.Count - 1);
                if (idx >= 0)
                    _originalParent.Controls.SetChildIndex(_view, idx);
            }
            _view.Dock = _originalDock;

            // ---- 4) 恢复浏览器 UI ----
            if (_menu != null) _menu.Visible = _savedMenuVisible;
            if (_toolbar != null) _toolbar.Visible = _savedToolbarVisible;
            if (_tabs != null) _tabs.Visible = _savedTabsVisible;

            IsFullscreen = false;
            _view = null;
            _originalParent = null;
        }

        // ============================================================
        //  键盘
        // ============================================================

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && IsFullscreen)
            {
                // 让 WebView2 退出网页级全屏（调用 document.exitFullscreen()）
                ExitWebFullscreen();
                e.Handled = true;
            }
            // F11 也可切换全屏
            if (e.KeyCode == Keys.F11)
            {
                if (IsFullscreen) ExitWebFullscreen();
                e.Handled = true;
            }
        }

        /// <summary>通知网页退出全屏（网页端会触发 fullscreenchange，然后 ContainsFullScreenElement 变 false，
        /// 进而自动调用我们的 ExitFullscreen）</summary>
        private static async void ExitWebFullscreen()
        {
            if (_view == null || _view.CoreWebView2 == null) return;
            try
            {
                // 先尝试让网页自己退出全屏
                await _view.CoreWebView2.ExecuteScriptAsync(
                    "if (document.fullscreenElement) document.exitFullscreen();");
            }
            catch { }
            // 兜底：如果网页没响应，直接恢复界面
            if (IsFullscreen) ExitFullscreen();
        }
    }
}
