// MainForm.cs —— 主窗口：菜单、工具栏、地址栏、标签页容器
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public class MainForm : Form, IMessageFilter
    {
        private TabManager _tabMgr;
        private TabControl _tabs;
        private BookmarkStore _bookmarks;
        private DownloadStore _downloads;
        private HistoryStore _history;

        // ---------- 自绘外壳（Vista 风格玻璃） ----------
        private TitleBar _titleBar;
        private GlassChrome _chrome;

        // ---------- 缩放相关 UI（「查看」菜单里的档位） ----------
        private ChromeMenuItem _miZoomIn;
        private ChromeMenuItem _miZoomOut;
        private ChromeMenuItem _miZoomReset;
        private ChromeMenuItem _miZoomList;

        public MainForm(string initialUrl)
        {
            // 玻璃层的前置条件：必须在句柄创建之前设好绘制样式。
            // 放到 OnHandleCreated 里就晚了 —— 那时窗口背景样式已经确定，
            // GDI 会把客户区涂成不透明色，Acrylic 再也透不出来。
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);

            Text = "LoongBrowser";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 760);
            MinimumSize = new Size(700, 480);

            // 无边框窗口没有系统缩放边框：自己留一圈 Padding 当拖拽热区。
            // 这一圈同时就是玻璃 —— Form 的 OnPaint 只清面不铺色，Acrylic 直接透出。
            // 内部控件全部缩在 Padding 以内，鼠标落在圈上时由 Form 作答 WM_NCHITTEST
            // （子控件不能代替窗口发起缩放循环，见 docs/adr/0007-resize-border-band.md）。
            Padding = new Padding(VistaTheme.ResizeBorderWidth);

            _bookmarks = new BookmarkStore();
            _downloads = new DownloadStore();
            _history = new HistoryStore();
            
            // 加载翻译配置
            TranslationService.LoadConfig();

            // 自绘标题栏：FormBorderStyle.None 后补回拖动、最大化、关闭
            // Dock 的排列顺序：先加 Fill（标签容器），再加 Dock.Top（外壳、标题栏），
            // 最后把它们整体挤进 Padding 以内 —— 边缘一圈留给缩放热区。
            FormBorderStyle = FormBorderStyle.None;
            _titleBar = new TitleBar(this);
            _titleBar.TitleText = Text;

            // 自绘玻璃外壳：菜单 + 工具栏 + 地址栏 + 标签头
            _chrome = new GlassChrome();
            _chrome.AddressSubmit += delegate(string text)
            {
                if (text == null) return;
                string t = text.Trim();
                if (t.Length == 0) return;
                _tabMgr.NavigateCurrent(NormalizeUrl(t));
            };
            _chrome.TabCloseRequested += delegate(int index)
            {
                if (_tabMgr != null) _tabMgr.CloseTab(index);
            };

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Visible = true;
            // 标签条由 GlassChrome 自绘，TabControl 只当 WebView2 容器用
            TabStrip.HideHeader(_tabs);

            // 添加顺序决定 z-order：Fill 的先加，Dock.Top 的后加
            Controls.Add(_tabs);
            Controls.Add(_chrome);
            Controls.Add(_titleBar);

            _tabMgr = new TabManager(_tabs);
            _tabMgr.Downloads = _downloads;
            _tabMgr.Bookmarks = _bookmarks;
            _tabMgr.History = _history;
            _tabMgr.TabChanged += OnTabChanged;
            _tabMgr.ZoomChanged += delegate { RefreshZoomUi(); };

            // 外壳内容依赖 _tabMgr，必须在它构造之后注入
            _chrome.AttachTabs(_tabs);
            _chrome.SetMenus(BuildMenus());
            _chrome.SetButtons(BuildToolButtons());

            // 书签增删后刷新正在显示的新标签页（书签墙）
            _bookmarks.Changed += delegate
            {
                if (_tabMgr != null) _tabMgr.RefreshNewTabPages();
            };

            // 启动窗口：保持"默认主页 = 空白页"的设定；
            // 只有"＋新标签"（NewTab(null)）才展示书签墙（NewTabPage.UseForStartup 可改为启动即显示）
            _tabMgr.NewTab(string.IsNullOrEmpty(initialUrl) && !NewTabPage.UseForStartup
                ? NewTabPage.HomeUrl
                : initialUrl);

            // 轮询接收后续实例转发来的 URL（单实例机制）
            _lastUrlTick = DateTime.Now.Ticks;
            var timer = new Timer();
            timer.Interval = 800;
            timer.Tick += delegate { CheckForwardedUrl(); };
            timer.Start();

            // 初始化全屏管理器（真全屏要连自绘标题栏一起隐藏）
            FullscreenManager.Init(this, _titleBar, _chrome, _tabs);

            // 网页缩放：菜单/工具栏的初始比例 + 进程内键盘与滚轮入口
            RefreshZoomUi();
            Application.AddMessageFilter(this);

            // 玻璃层不在这里应用：窗口可见之前的 backdrop 会被窗口创建的后续流程覆盖，
            // 首帧仍然是不透明的一块（实测见 GlassManager 顶部注释）。改到 OnShown。
        }

        // ---------- 无边框窗口的缩放边框 ----------
        //
        // 刻意**不**加 WS_THICKFRAME：实测（二分定位）它会让 DWM 在窗口失活时停用
        // Acrylic —— 一旦失焦（点菜单、切到别的应用、点桌面），整个窗口变成不透明
        // 纯色块，玻璃消失且无法恢复。用户要求"玻璃常驻、不做降级"，所以缩放改由
        // 本类自己实现（见 WindowResizer 顶部说明与 ADR-0007）。

        /// <summary>保留最小化 / 最大化按钮语义（不加 WS_THICKFRAME）</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= WindowResizer.WS_MINIMIZEBOX | WindowResizer.WS_MAXIMIZEBOX;
                return cp;
            }
        }

        /// <summary>窗口已显示且首帧画完：此刻应用 Acrylic 才能一出现就是玻璃</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 见 docs/adr/0001-use-acrylic-instead-of-aero-glass.md
            GlassManager.Apply(Handle, false);
            ApplyGlassToSurface();

            // 尺寸变化后整窗重绘：玻璃区的像素只能靠"清回透明"来更新，
            // 不重画的话旧宽度上的地址栏白条会留在窗口表面。
            ResizeEnd += delegate { ApplyGlassToSurface(); };
            SizeChanged += delegate
            {
                if (WindowState != FormWindowState.Minimized) BeginInvoke(new Action(ApplyGlassToSurface));
            };
        }

        // ---------- 边缘拖拽缩放（自实现） ----------

        private WindowResizer.DragSession _resizeSession;
        private WindowResizer.Edge _hotEdge = WindowResizer.Edge.None;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;

            WindowResizer.Edge edge = WindowResizer.HitTest(e.Location, ClientSize);
            if (edge == WindowResizer.Edge.None) return;

            _resizeSession = new WindowResizer.DragSession(
                this, edge, Bounds, Cursor.Position, MinimumSize);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_resizeSession != null)
            {
                _resizeSession.Update(Cursor.Position);
                return;
            }

            // 悬停反馈：边缘带上的指针形状
            WindowResizer.Edge edge = WindowState == FormWindowState.Maximized
                ? WindowResizer.Edge.None
                : WindowResizer.HitTest(e.Location, ClientSize);
            if (edge != _hotEdge)
            {
                _hotEdge = edge;
                Cursor = WindowResizer.CursorFor(edge);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_resizeSession == null) return;
            _resizeSession = null;
            Capture = false;
            ApplyGlassToSurface();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_resizeSession != null) return;
            if (_hotEdge != WindowResizer.Edge.None)
            {
                _hotEdge = WindowResizer.Edge.None;
                Cursor = Cursors.Default;
            }
        }

        /// <summary>整窗重绘一遍：标题栏与外壳各自先清面再画前景</summary>
        private void ApplyGlassToSurface()
        {
            if (IsDisposed) return;
            Invalidate(true);
            Update();
        }

        // ---------- 玻璃层 ----------

        /// <summary>玻璃当前是否已生效（供测试与诊断查询）</summary>
        public bool GlassApplied { get { return GlassManager.IsApplied; } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 故意留空：不绘制背景，露出 Acrylic。
            // 若这里调用 base.OnPaintBackground，GDI 会把客户区涂成不透明色，玻璃就看不见了。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 客户区里没有被子控件覆盖的只有四周那圈缩放边带：先清面露出玻璃，
            // 再补上外壳边缘的高光与阴影，让这圈和内部外壳连成一片。
            GlassManager.ClearSurface(e.Graphics, new Rectangle(0, 0, Width, Height));

            if (Padding.Left > 0)
                VistaTheme.DrawResizeBand(e.Graphics, ClientRectangle);
        }

        /// <summary>当前标签的缩放比例（读数，供状态展示与测试使用）</summary>
        public double CurrentZoom
        {
            get { return _tabMgr == null ? ZoomManager.SessionDefault : _tabMgr.ActiveZoom(); }
        }

        /// <summary>窗口关闭时摘掉全局消息过滤器（多窗口各挂各的，不留在链上）</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Application.RemoveMessageFilter(this); } catch (Exception) { }
            base.OnFormClosed(e);
        }

        private long _lastUrlTick;

        private void CheckForwardedUrl()
        {
            try
            {
                string path = Program.PendingUrlFile;
                if (!File.Exists(path)) return;
                // 原子认领：多个窗口同时轮询时，只有认领成功（重命名成功）的窗口处理该 URL
                string claim = path + ".claim";
                try { File.Delete(claim); } catch (Exception) { }
                try { File.Move(path, claim); } catch (Exception) { return; }
                string url = File.ReadAllText(claim).Trim();
                File.Delete(claim);
                if (url.Length == 0) return;
                _lastUrlTick = DateTime.Now.Ticks;
                _tabMgr.NewTab(url);
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
            catch (Exception) { }
        }

        // ---------- UI 构建 ----------

        /// <summary>构建顶部菜单（交给自绘外壳渲染）</summary>
        private List<ChromeMenuItem> BuildMenus()
        {
            var menus = new List<ChromeMenuItem>();

            menus.Add(ChromeMenuItem.Sub("书签(&B)",
                ChromeMenuItem.Item("收藏本页", delegate { AddBookmark(); }),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("书签管理...", delegate
                {
                    new BookmarkDialog(_bookmarks, url => _tabMgr.NewTab(url)).Show(this);
                })));

            menus.Add(ChromeMenuItem.Sub("下载(&D)",
                ChromeMenuItem.Item("下载管理...", delegate
                {
                    new DownloadDialog(_downloads).Show(this);
                })));

            menus.Add(ChromeMenuItem.Sub("历史(&H)",
                ChromeMenuItem.Item("查看历史记录...", delegate
                {
                    new HistoryDialog(_history, url => _tabMgr.NewTab(url)).Show(this);
                }),
                ChromeMenuItem.Item("清空全部历史记录", delegate { ClearAllHistory(); })));

            menus.Add(BuildViewMenu());

            menus.Add(ChromeMenuItem.Sub("工具(&T)",
                ChromeMenuItem.Item("清理缓存", delegate
                {
                    BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                        CoreWebView2BrowsingDataKinds.DiskCache, "清理缓存");
                }),
                ChromeMenuItem.Item("清理历史记录", delegate
                {
                    BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                        CoreWebView2BrowsingDataKinds.BrowsingHistory, "清理历史记录");
                }),
                ChromeMenuItem.Item("清理 Cookie", delegate
                {
                    BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                        CoreWebView2BrowsingDataKinds.Cookies, "清理 Cookie（将退出所有网站的登录状态）");
                }),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Check("新标签页显示书签", NewTabPage.Enabled, delegate
                {
                    NewTabPage.Enabled = !NewTabPage.Enabled;
                    _tabMgr.RefreshNewTabPages();
                }),
                ChromeMenuItem.Check("小窗默认置顶", PopupWindow.DefaultAlwaysOnTop, delegate
                {
                    PopupWindow.DefaultAlwaysOnTop = !PopupWindow.DefaultAlwaysOnTop;
                }),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("翻译选中文字", delegate { TranslateSelectedText(); }),
                ChromeMenuItem.Item("翻译当前页面", delegate { TranslateCurrentPage(); }),
                ChromeMenuItem.Item("翻译 API 配置...", delegate { ShowTranslationConfig(); }),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("设为默认浏览器...", delegate { SetDefaultBrowser(); }),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("关于 LoongBrowser", delegate { ShowAbout(); })));

            return menus;
        }

        /// <summary>「查看」菜单：放大 / 缩小 / 实际大小 + 预设档位列表</summary>
        private ChromeMenuItem BuildViewMenu()
        {
            _miZoomIn = ChromeMenuItem.Item("放大", delegate { ZoomIn(); });
            _miZoomIn.Shortcut = "Ctrl + +";
            _miZoomOut = ChromeMenuItem.Item("缩小", delegate { ZoomOut(); });
            _miZoomOut.Shortcut = "Ctrl + -";
            _miZoomReset = ChromeMenuItem.Item("实际大小（100%）", delegate { ResetZoom(); });
            _miZoomReset.Shortcut = "Ctrl + 0";

            _miZoomList = ChromeMenuItem.Sub("缩放（100%）");
            _miZoomList.Children = BuildZoomPresetItems();

            return ChromeMenuItem.Sub("查看(&V)",
                _miZoomIn, _miZoomOut, _miZoomReset,
                ChromeMenuItem.Separator(),
                _miZoomList);
        }

        /// <summary>生成预设档位菜单项（Tag 用比例值表示，放在 Click 闭包里）</summary>
        private List<ChromeMenuItem> BuildZoomPresetItems()
        {
            var items = new List<ChromeMenuItem>();
            foreach (double p in ZoomManager.Presets)
            {
                double preset = p;
                var mi = ChromeMenuItem.Item(ZoomManager.Percent(preset), delegate { SetZoom(preset); });
                mi.Tag = (object)preset;
                items.Add(mi);
            }
            return items;
        }

        /// <summary>构建工具栏按钮（交给自绘外壳渲染）</summary>
        private List<ChromeToolButton> BuildToolButtons()
        {
            var list = new List<ChromeToolButton>();

            list.Add(ChromeToolButton("back", "后退", delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CanGoBack) v.GoBack();
            }));
            list.Add(ChromeToolButton("forward", "前进", delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CanGoForward) v.GoForward();
            }));
            list.Add(ChromeToolButton("refresh", "刷新", delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CoreWebView2 != null) v.CoreWebView2.Reload();
            }));
            list.Add(SeparatorButton());
            list.Add(ChromeToolButton("copy", "复制地址", delegate
            {
                string text = _chrome.AddressText;
                if (!string.IsNullOrEmpty(text))
                {
                    try { Clipboard.SetText(text); } catch (Exception) { }
                }
            }));
            list.Add(ChromeToolButton("paste", "粘贴", delegate
            {
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip))
                    {
                        _chrome.AddressText = clip;
                        _chrome.FocusAddress();
                    }
                }
                catch (Exception) { }
            }));
            list.Add(SeparatorButton());
            list.Add(ChromeToolButton("star", "收藏本页", delegate { AddBookmark(); }));
            list.Add(ChromeToolButton("translate", "翻译", delegate { TranslateSelectedText(); }));
            list.Add(SeparatorButton());
            list.Add(ChromeToolButton("zoom", "网页缩放", delegate { OpenZoomMenu(); }));
            list.Add(SeparatorButton());
            list.Add(ChromeToolButton("plus", "新标签", delegate { _tabMgr.NewTab(null); }));
            list.Add(ChromeToolButton("close", "关闭当前标签", delegate { _tabMgr.CloseCurrent(); }));

            return list;
        }

        private static ChromeToolButton ChromeToolButton(string icon, string tooltip, Action click)
        {
            var b = new ChromeToolButton();
            b.Icon = icon;
            b.Tooltip = tooltip;
            b.Click = click;
            return b;
        }

        private static ChromeToolButton SeparatorButton()
        {
            var b = new ChromeToolButton();
            b.Separator = true;
            return b;
        }

        /// <summary>工具栏缩放下拉：在弹出的菜单里列预设档位</summary>
        private void OpenZoomMenu()
        {
            RefreshZoomUi();
            var items = BuildZoomPresetItems();
            var dd = new ChromeDropDown(items, 140);

            // 定位到缩放按钮下方
            Point p = _chrome.PointToScreen(new Point(_chrome.Width - 200, VistaTheme.TitleBarHeight
                + VistaTheme.MenuBarHeight + VistaTheme.ToolBarHeight));
            dd.ShowAt(p);
        }

        // ---------- 功能动作 ----------

        /// <summary>清空全部历史：应用自己记的那份 + 内核的历史库，避免两处不一致</summary>
        private async void ClearAllHistory()
        {
            var dr = MessageBox.Show(this, "确定要清空全部历史记录吗？此操作不可撤销。", "清空历史记录",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;

            _history.Clear();
            bool kernelOk = await BrowserCleaner.ClearAsync(() => _tabMgr.ActiveCore(),
                CoreWebView2BrowsingDataKinds.BrowsingHistory);
            MessageBox.Show(this,
                kernelOk ? "历史记录已清空。" : "应用内的历史记录已清空；内核历史需要先打开一个网页后再清理。",
                "提示");
        }

        private void NavigateFromAddress()
        {
            if (_chrome == null) return;
            string t = _chrome.AddressText.Trim();
            if (t.Length == 0) return;
            _tabMgr.NavigateCurrent(NormalizeUrl(t));
        }

        /// <summary>
        /// 输入归一化：补全协议；本地文件/文件夹路径转成 file:// URL；非网址则作为搜索词。
        /// 关键：必须先识别本地路径。双击 .html 文件时，Windows 按注册表命令
        /// "LoongBrowser.exe" "%1" 把裸路径（如 C:\dir\a.html）作为命令行参数传进来；
        /// 若按"含点号的网址"处理，会拼成 https://C:\dir\a.html，内核把盘符 C 当成主机名
        /// 去做 DNS 解析，于是报 ERR_NAME_NOT_RESOLVED（找不到 c 的服务器 IP 地址）。
        /// </summary>
        public static string NormalizeUrl(string input)
        {
            if (input == null) return "about:blank";
            input = input.Trim();
            if (input.Length == 0) return "about:blank";

            // 注册表 %1 展开后可能残留成对引号
            if (input.Length >= 2 && input[0] == '"' && input[input.Length - 1] == '"')
                input = input.Substring(1, input.Length - 2).Trim();

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
                return input;

            // 本地路径优先于"含点号即网址"的启发式判断
            if (IsLocalPath(input) || File.Exists(input))
                return PathToFileUrl(input);

            if (input.Contains(".") && !input.Contains(" ")) return "https://" + input;
            return "https://www.bing.com/search?q=" + Uri.EscapeDataString(input);
        }

        /// <summary>是否 Windows 本地路径：盘符路径 C:\ 或 C:/ ，或 UNC 路径 \\server\share</summary>
        public static bool IsLocalPath(string s)
        {
            if (s == null || s.Length < 2) return false;
            if (char.IsLetter(s[0]) && s[1] == ':' && (s.Length == 2 || s[2] == '\\' || s[2] == '/')) return true;
            if (s[0] == '\\' && s[1] == '\\') return true;
            return false;
        }

        /// <summary>本地路径 → 规范的 file:/// URL（正确转义空格、中文、# 等字符）</summary>
        public static string PathToFileUrl(string path)
        {
            try
            {
                return new Uri(Path.GetFullPath(path)).AbsoluteUri;
            }
            catch (Exception)
            {
                string p = path.Replace('\\', '/');
                if (p.StartsWith("//")) return new Uri("file:" + p).AbsoluteUri;      // UNC：\\srv\share → file://srv/share
                if (p.StartsWith("/")) return new Uri("file://" + p).AbsoluteUri;
                return new Uri("file:///" + p).AbsoluteUri;                           // C:/... → file:///C:/...
            }
        }

        private void AddBookmark()
        {
            var core = _tabMgr.ActiveCore();
            if (core == null)
            {
                MessageBox.Show("当前没有可收藏的页面。", "提示");
                return;
            }
            string title = core.DocumentTitle;
            string url = core.Source;
            if (string.IsNullOrEmpty(url) || url == "about:blank")
            {
                MessageBox.Show("空白页无需收藏。", "提示");
                return;
            }
            _bookmarks.Add(string.IsNullOrEmpty(title) ? url : title, url);
            MessageBox.Show("已收藏：" + (string.IsNullOrEmpty(title) ? url : title), "书签");
        }

        private void SetDefaultBrowser()
        {
            try
            {
                DefaultBrowser.Register(Application.ExecutablePath);
                DefaultBrowser.OpenSettings();
                MessageBox.Show(
                    "已完成系统注册。\n" +
                    "请在打开的\"设置 → 应用 → 默认应用\"中找到 LoongBrowser，\n" +
                    "将其设为 http/https 的默认打开方式（Windows 安全机制要求此确认步骤）。",
                    "设为默认浏览器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("注册失败：" + ex.Message, "错误");
            }
        }

        private void ShowAbout()
        {
            string ver = _tabMgr.ActiveBrowserVersion();
            MessageBox.Show(
                "LoongBrowser v1.1.1\n" +
                "极简 Chromium 内核浏览器（WebView2）\n" +
                (ver.Length > 0 ? "内核版本：" + ver + "\n" : "") +
                "数据目录：%APPDATA%\\LoongBrowser",
                "关于 LoongBrowser");
        }

        // ---------- 翻译功能 ----------

        /// <summary>翻译选中文字</summary>
        private void TranslateSelectedText()
        {
            var core = _tabMgr.ActiveCore();
            if (core == null)
            {
                MessageBox.Show("当前没有可翻译的页面。", "提示");
                return;
            }

            // 尝试获取选中的文字
            string selectedText = "";
            try
            {
                // 通过 JavaScript 获取选中文本
                var task = core.ExecuteScriptAsync("window.getSelection().toString()");
                task.Wait(1000);
                if (task.IsCompleted)
                {
                    string result = task.Result;
                    // 去掉 JSON 引号
                    if (result != null && result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
                    {
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        selectedText = serializer.Deserialize<string>(result) ?? "";
                    }
                }
            }
            catch (Exception) { }

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                // 没有选中文字，提示用户
                var dr = MessageBox.Show(
                    "没有选中任何文字。\n\n是否要翻译整个页面？",
                    "翻译",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dr == DialogResult.Yes)
                {
                    TranslateCurrentPage();
                }
                return;
            }

            // 显示翻译对话框
            using (var dlg = new TranslationDialog(selectedText))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>翻译当前页面</summary>
        private async void TranslateCurrentPage()
        {
            var core = _tabMgr.ActiveCore();
            if (core == null)
            {
                MessageBox.Show("当前没有可翻译的页面。", "提示");
                return;
            }

            if (!TranslationService.IsConfigured)
            {
                var dr = MessageBox.Show(
                    "使用翻译功能需要配置百度翻译 API 密钥。\n\n是否现在配置？",
                    "翻译配置",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dr == DialogResult.Yes)
                {
                    ShowTranslationConfig();
                }
                return;
            }

            // 获取页面标题
            string title = core.DocumentTitle ?? "";
            
            // 获取页面文本内容（通过 JavaScript）
            string pageText = "";
            try
            {
                // 提取页面正文文本，限制长度
                string script = @"
                    (function() {
                        var text = document.body.innerText || '';
                        if (text.length > 5000) text = text.substring(0, 5000) + '...';
                        return text;
                    })()";
                var result = await core.ExecuteScriptAsync(script);
                if (result != null && result.Length >= 2)
                {
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    pageText = serializer.Deserialize<string>(result) ?? "";
                }
            }
            catch (Exception) { }

            if (string.IsNullOrWhiteSpace(pageText))
            {
                MessageBox.Show("无法获取页面文本内容。", "提示");
                return;
            }

            // 显示翻译对话框
            using (var dlg = new TranslationDialog(pageText))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>显示翻译 API 配置</summary>
        private void ShowTranslationConfig()
        {
            using (var dlg = new TranslationConfigDialog())
            {
                dlg.ShowDialog(this);
            }
        }

        // ---------- 网页缩放 ----------

        private void ZoomIn()
        {
            if (_tabMgr != null) _tabMgr.ZoomIn();
            RefreshZoomUi();
        }

        private void ZoomOut()
        {
            if (_tabMgr != null) _tabMgr.ZoomOut();
            RefreshZoomUi();
        }

        private void ResetZoom()
        {
            if (_tabMgr != null) _tabMgr.ResetZoom();
            RefreshZoomUi();
        }

        private void SetZoom(double factor)
        {
            if (_tabMgr != null) _tabMgr.SetZoom(factor);
            RefreshZoomUi();
        }

        /// <summary>把当前比例刷到菜单勾选、菜单/工具栏的百分比文本上</summary>
        private void RefreshZoomUi()
        {
            if (_tabMgr == null) return;
            double cur = _tabMgr.ActiveZoom();
            string pct = ZoomManager.Percent(cur);

            if (_miZoomList != null) _miZoomList.Text = "缩放（" + pct + "）";

            // 到达 50% / 200% 边界时置灰对应的步进命令（预设档位仍可直接点选）
            if (_miZoomIn != null) _miZoomIn.Enabled = !ZoomManager.AtMax(cur);
            if (_miZoomOut != null) _miZoomOut.Enabled = !ZoomManager.AtMin(cur);

            CheckZoomPreset(_miZoomList, cur);
        }

        /// <summary>按当前比例勾中预设档位（同一档才算选中，按 1% 比较）</summary>
        private static void CheckZoomPreset(ChromeMenuItem owner, double current)
        {
            if (owner == null || owner.Children == null) return;
            foreach (ChromeMenuItem mi in owner.Children)
            {
                if (mi == null || !(mi.Tag is double)) continue;
                mi.Checked = ZoomManager.SamePercent((double)mi.Tag, current);
            }
        }

        // ---------- 缩放的键盘 / 滚轮入口（进程内消息过滤器） ----------
        //
        // 只拦“发给本程序窗口”的消息：焦点在地址栏等宿主控件上时，Ctrl+= / Ctrl+- / Ctrl+0
        // 与 Ctrl+滚轮都排在宿主线程的消息队列里，这里拦截一次即可，命中即吞掉。
        //
        // 焦点在网页里时，按键/滚轮的接收方是 WebView2 内核**进程**里的窗口（不同 pid），
        // 根本不会进本程序的消息队列 —— 要在那种情形下接管，只能装系统级低级钩子
        // （WH_KEYBOARD_LL / WH_MOUSE_LL，全局监听全键盘鼠标）；权衡后明确不做：
        // 不为一个快捷键引入系统级钩子，网页有焦点时的缩放交给内核自身的快捷键，
        // 任何情况下都还能用「查看」菜单与工具栏下拉完成缩放。
        //
        // 菜单项上的 ShortcutKeyDisplayString 只是展示文字，不注册菜单快捷键，
        // 避免同一次按键被两条链各响应一次。

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_MOUSEWHEEL = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (_tabMgr == null) return false;
            // 多窗口各挂了一个过滤器：先确认消息是发给本窗口的，免得把缩放加到别的窗口
            if (!IsForThisWindow(m.HWnd)) return false;

            switch (m.Msg)
            {
                case WM_KEYDOWN:
                    return FilterZoomKey(m);
                case WM_MOUSEWHEEL:
                    return FilterZoomWheel(m);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 消息归属判定：拿到目标窗口往上找宿主 Form（内核的子窗口也能归位到承载它的 WebView2 控件）。
        /// 认不出归属时退回“本窗口是活动窗口”这条判据。
        /// </summary>
        private bool IsForThisWindow(IntPtr hwnd)
        {
            try
            {
                Control c = Control.FromChildHandle(hwnd);
                if (c != null)
                {
                    Form owner = c.FindForm();
                    if (owner != null) return owner == this;
                }
                return Form.ActiveForm == this;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Ctrl+= / Ctrl+- / Ctrl+0（含 Ctrl+Shift+= 与小键盘写法），命中即吞掉</summary>
        private bool FilterZoomKey(Message m)
        {
            Keys code = ((Keys)(long)m.WParam) & Keys.KeyCode;
            ZoomAction action = ZoomManager.KeyAction(
                (Control.ModifierKeys & Keys.Control) != 0, code);
            if (action == ZoomAction.None) return false;

            if (action == ZoomAction.ZoomIn) ZoomIn();
            else if (action == ZoomAction.ZoomOut) ZoomOut();
            else ResetZoom();
            return true;
        }

        /// <summary>Ctrl+滚轮：在鼠标所在的标签上缩放；没压在网页上的滚轮保持原样</summary>
        private bool FilterZoomWheel(Message m)
        {
            int delta = (short)(((long)m.WParam) >> 16);           // HIWORD = 本格位移（±120 一格）
            int steps = ZoomManager.WheelSteps(
                (Control.ModifierKeys & Keys.Control) != 0, delta);
            if (steps == 0) return false;

            bool hit = _tabMgr.ZoomAtPoint(Cursor.Position, steps);
            if (hit) RefreshZoomUi();
            return hit;                                            // 只有真正在网页上缩放时才吞掉滚轮
        }

        // ---------- 标签联动 ----------

        private void OnTabChanged(TabInfo tab)
        {
            if (tab == null || tab.View == null) return;
            if (tab.Page != _tabs.SelectedTab) return;
            RefreshZoomUi();                // 每个标签有自己的比例，切标签要刷新显示
            if (_chrome == null) return;
            TextBox box = _chrome.AddressBox;
            if (box == null || box.Focused) return;
            try
            {
                string src = tab.View.Source != null ? tab.View.Source.ToString() : "";
                // 新标签页/空白页不把 about:blank 显示到地址栏
                _chrome.AddressText = NewTabPage.IsInternalUrl(src) ? "" : src;
            }
            catch (Exception) { }
        }
    }
}
