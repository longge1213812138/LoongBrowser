// MainForm.cs —— 主窗口：菜单、工具栏、地址栏、标签页容器
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public class MainForm : Form
    {
        private TabManager _tabMgr;
        private TabControl _tabs;
        private MenuStrip _menu;
        private ToolStrip _toolbar;
        private ToolStripTextBox _addressBox;
        private BookmarkStore _bookmarks;
        private DownloadStore _downloads;
        private HistoryStore _history;

        public MainForm(string initialUrl)
        {
            Text = "LoongBrowser";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 760);
            MinimumSize = new Size(700, 480);

            _bookmarks = new BookmarkStore();
            _downloads = new DownloadStore();
            _history = new HistoryStore();

            BuildMenu();
            BuildToolbar();

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            TabStrip.Style(_tabs);            // 更大字号、统一尺寸、标签之间留间隔

            Controls.Add(_tabs);
            Controls.Add(_toolbar);
            Controls.Add(_menu);

            _tabMgr = new TabManager(_tabs);
            _tabMgr.Downloads = _downloads;
            _tabMgr.Bookmarks = _bookmarks;
            _tabMgr.History = _history;
            _tabMgr.TabChanged += OnTabChanged;

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

        private void BuildMenu()
        {
            _menu = new MenuStrip();

            var mBookmark = new ToolStripMenuItem("书签(&B)");
            mBookmark.DropDownItems.Add("收藏本页", null, delegate { AddBookmark(); });
            mBookmark.DropDownItems.Add(new ToolStripSeparator());
            mBookmark.DropDownItems.Add("书签管理...", null, delegate
            {
                new BookmarkDialog(_bookmarks, url => _tabMgr.NewTab(url)).Show(this);
            });

            var mDownload = new ToolStripMenuItem("下载(&D)");
            mDownload.DropDownItems.Add("下载管理...", null, delegate
            {
                new DownloadDialog(_downloads).Show(this);
            });

            var mHistory = new ToolStripMenuItem("历史(&H)");
            mHistory.DropDownItems.Add("查看历史记录...", null, delegate
            {
                new HistoryDialog(_history, url => _tabMgr.NewTab(url)).Show(this);
            });
            mHistory.DropDownItems.Add("清空全部历史记录", null, delegate { ClearAllHistory(); });

            var mTool = new ToolStripMenuItem("工具(&T)");
            mTool.DropDownItems.Add("清理缓存", null, delegate
            {
                BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                    CoreWebView2BrowsingDataKinds.DiskCache, "清理缓存");
            });
            mTool.DropDownItems.Add("清理历史记录", null, delegate
            {
                BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                    CoreWebView2BrowsingDataKinds.BrowsingHistory, "清理历史记录");
            });
            mTool.DropDownItems.Add("清理 Cookie", null, delegate
            {
                BrowserCleaner.Clear(this, () => _tabMgr.ActiveCore(),
                    CoreWebView2BrowsingDataKinds.Cookies, "清理 Cookie（将退出所有网站的登录状态）");
            });
            mTool.DropDownItems.Add(new ToolStripSeparator());
            var miNewTab = new ToolStripMenuItem("新标签页显示书签");
            miNewTab.CheckOnClick = true;
            miNewTab.Checked = NewTabPage.Enabled;
            miNewTab.Click += delegate(object s, EventArgs e)
            {
                var mi = s as ToolStripMenuItem;
                if (mi == null) return;
                NewTabPage.Enabled = mi.Checked;
                _tabMgr.RefreshNewTabPages();     // 关掉时新标签页会回到空白页
            };
            mTool.DropDownItems.Add(miNewTab);
            var miPopupTop = new ToolStripMenuItem("小窗默认置顶");
            miPopupTop.CheckOnClick = true;
            miPopupTop.Checked = PopupWindow.DefaultAlwaysOnTop;
            miPopupTop.ToolTipText = "B 站画中画等弹出的小窗是否始终显示在最前面";
            miPopupTop.Click += delegate(object s, EventArgs e)
            {
                var mi = s as ToolStripMenuItem;
                if (mi == null) return;
                PopupWindow.DefaultAlwaysOnTop = mi.Checked;
            };
            mTool.DropDownItems.Add(miPopupTop);
            mTool.DropDownItems.Add("设为默认浏览器...", null, delegate { SetDefaultBrowser(); });
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add("关于 LoongBrowser", null, delegate { ShowAbout(); });

            _menu.Items.Add(mBookmark);
            _menu.Items.Add(mDownload);
            _menu.Items.Add(mHistory);
            _menu.Items.Add(mTool);
        }

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip();
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _toolbar.Padding = new Padding(4, 2, 4, 2);

            var btnBack = new ToolStripButton("◀");
            btnBack.ToolTipText = "后退";
            btnBack.Click += delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CanGoBack) v.GoBack();
            };

            var btnForward = new ToolStripButton("▶");
            btnForward.ToolTipText = "前进";
            btnForward.Click += delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CanGoForward) v.GoForward();
            };

            var btnRefresh = new ToolStripButton("⟳");
            btnRefresh.ToolTipText = "刷新";
            btnRefresh.Click += delegate
            {
                var v = _tabMgr.ActiveView();
                if (v != null && v.CoreWebView2 != null) v.CoreWebView2.Reload();
            };

            _addressBox = new ToolStripTextBox();
            _addressBox.BorderStyle = BorderStyle.FixedSingle;
            _addressBox.AutoSize = false;
            _addressBox.Width = 560;
            _addressBox.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == (char)13)
                {
                    e.Handled = true;
                    NavigateFromAddress();
                }
            };

            var btnNewTab = new ToolStripButton("＋新标签");
            btnNewTab.Click += delegate { _tabMgr.NewTab(null); };

            var btnCloseTab = new ToolStripButton("✕关闭");
            btnCloseTab.Click += delegate { _tabMgr.CloseCurrent(); };

            var btnStar = new ToolStripButton("☆ 收藏");
            btnStar.Click += delegate { AddBookmark(); };

            _toolbar.Items.Add(btnBack);
            _toolbar.Items.Add(btnForward);
            _toolbar.Items.Add(btnRefresh);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_addressBox);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnStar);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnNewTab);
            _toolbar.Items.Add(btnCloseTab);
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
        {            string t = _addressBox.Text.Trim();
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
                "LoongBrowser v1.1.0\n" +
                "极简 Chromium 内核浏览器（WebView2）\n" +
                (ver.Length > 0 ? "内核版本：" + ver + "\n" : "") +
                "数据目录：%APPDATA%\\LoongBrowser",
                "关于 LoongBrowser");
        }

        // ---------- 标签联动 ----------

        private void OnTabChanged(TabInfo tab)
        {
            if (tab == null || tab.View == null) return;
            if (tab.Page != _tabs.SelectedTab) return;
            if (_addressBox == null) return;
            if (!_addressBox.Focused)
            {
                try
                {
                    string src = tab.View.Source != null ? tab.View.Source.ToString() : "";
                    // 新标签页/空白页不把 about:blank 显示到地址栏
                    _addressBox.Text = NewTabPage.IsInternalUrl(src) ? "" : src;
                }
                catch (Exception) { }
            }
        }
    }
}
