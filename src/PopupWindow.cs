// PopupWindow.cs —— 程序化弹出的小窗（B 站画中画小窗、OAuth 授权窗、打印预览等）
//
// 为什么自建窗口而不是交回内核开原生弹窗：
//   内核开的原生弹窗不归我们管，**没法做"窗口置顶"**。自建窗口后就能：
//     · 顶部给一个「窗口置顶」开关（TopMost），切到别的应用也压在最先
//     · 按站点请求的尺寸开窗（window.open 的 width/height）
//     · 置顶默认值可记住（下次开小窗沿用），主窗口「工具」菜单也能改
// 内核侧通过 NewWindowRequested 的 GetDeferral + NewWindow 把新窗口交给发起方，
// 这样 opener 拿到的仍是真正的 window 句柄（B 站小窗需要往里写播放器）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    /// <summary>小窗的可持久化设置：%APPDATA%\LoongBrowser\popup.json</summary>
    public class PopupSettings
    {
        public bool AlwaysOnTop = true;
    }

    public class PopupWindow : Form
    {
        /// <summary>顶部工具条高度（置顶开关所在的那一条）</summary>
        public const int BarHeight = 30;

        private static readonly string SettingsPath = Path.Combine(AppPaths.DataDir, "popup.json");
        private static PopupSettings _settings;
        private static readonly List<PopupWindow> _open = new List<PopupWindow>();
        private static readonly object _lock = new object();

        private readonly WebView2 _view;
        private readonly CheckBox _topBox;

        /// <summary>当前打开的小窗快照</summary>
        public static PopupWindow[] OpenWindows()
        {
            lock (_lock) { return _open.ToArray(); }
        }

        /// <summary>新建小窗是否默认置顶（会落盘记住）</summary>
        public static bool DefaultAlwaysOnTop
        {
            get { return Settings.AlwaysOnTop; }
            set
            {
                Settings.AlwaysOnTop = value;
                JsonStore.Save(SettingsPath, Settings);
            }
        }

        private static PopupSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = JsonStore.Load<PopupSettings>(SettingsPath);
                    if (_settings == null) _settings = new PopupSettings();
                }
                return _settings;
            }
        }

        public PopupWindow()
        {
            Text = "LoongBrowser 小窗";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowIcon = false;
            ClientSize = new Size(480, 320 + BarHeight);

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = BarHeight;
            bar.Padding = new Padding(8, 4, 8, 0);
            bar.WrapContents = false;

            _topBox = new CheckBox();
            _topBox.Text = "窗口置顶";
            _topBox.AutoSize = true;
            _topBox.Checked = DefaultAlwaysOnTop;
            _topBox.CheckedChanged += delegate { ApplyAlwaysOnTop(_topBox.Checked, true); };
            bar.Controls.Add(_topBox);

            _view = new WebView2();
            _view.Dock = DockStyle.Fill;

            Controls.Add(_view);     // 先加填充区，再加顶部条
            Controls.Add(bar);
            TopMost = _topBox.Checked;

            lock (_lock) { _open.Add(this); }
        }

        /// <summary>内容区尺寸（不含顶部工具条），对应 window.open 请求的 width/height</summary>
        public void SetContentSize(int width, int height)
        {
            try
            {
                int w = Math.Max(160, width);
                int h = Math.Max(120, height);
                ClientSize = new Size(w, h + BarHeight);
            }
            catch (Exception) { }
        }

        /// <summary>内核初始化（要在窗口显示之后调用）</summary>
        public async Task InitAsync(CoreWebView2Environment env)
        {
            await _view.EnsureCoreWebView2Async(env);
            try
            {
                // 页面自己调用 window.close()（站点小窗的关闭按钮）时，把宿主窗口一起关掉，
                // 否则会留下一个空白孤儿窗口
                _view.CoreWebView2.WindowCloseRequested += delegate
                {
                    try { Close(); } catch (Exception) { }
                };
            }
            catch (Exception) { }
        }

        /// <summary>本小窗的内核（交给 NewWindowRequested 的 NewWindow）</summary>
        public CoreWebView2 Core
        {
            get { return _view == null ? null : _view.CoreWebView2; }
        }

        /// <summary>当前是否置顶</summary>
        public bool IsAlwaysOnTop
        {
            get { return TopMost; }
        }

        /// <summary>程序化切换置顶（等价于勾选顶部那个开关，同样会记住选择）</summary>
        public void SetAlwaysOnTop(bool on)
        {
            if (_topBox == null) { ApplyAlwaysOnTop(on, true); return; }
            if (_topBox.Checked == on) ApplyAlwaysOnTop(on, true);
            else _topBox.Checked = on;      // 走 CheckedChanged，统一处理
        }

        private void ApplyAlwaysOnTop(bool on, bool persist)
        {
            try { TopMost = on; } catch (Exception) { }
            if (persist) DefaultAlwaysOnTop = on;   // 记住选择，下次开小窗沿用
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            lock (_lock) { _open.Remove(this); }
        }
    }
}
