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
        private MenuStrip _menu;
        private ToolStrip _toolbar;
        private ToolStripTextBox _addressBox;
        private BookmarkStore _bookmarks;
        private DownloadStore _downloads;
        private HistoryStore _history;

        // ---------- 缩放相关 UI（「查看」菜单 + 工具栏缩放下拉） ----------
        private ToolStripMenuItem _miZoomIn;
        private ToolStripMenuItem _miZoomOut;
        private ToolStripMenuItem _miZoomReset;
        private ToolStripMenuItem _miZoomList;
        private ToolStripDropDownButton _zoomButton;

        public MainForm(string initialUrl)
        {
            Text = "LoongBrowser";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 760);
            MinimumSize = new Size(700, 480);

            _bookmarks = new BookmarkStore();
            _downloads = new DownloadStore();
            _history = new HistoryStore();
            
            // 加载翻译配置
            TranslationService.LoadConfig();

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
            _tabMgr.ZoomChanged += delegate { RefreshZoomUi(); };

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

            // 订阅暗色模式变化事件，更新界面样式
            DarkModeManager.DarkModeChanged += OnDarkModeChanged;
            // 应用初始暗色模式设置
            DarkModeManager.ApplyToForm(this);
            ApplyDarkModeToToolbar();

            // 初始化全屏管理器
            FullscreenManager.Init(this, _menu, _toolbar, _tabs);

            // 网页缩放：菜单/工具栏的初始比例 + 进程内键盘与滚轮入口
            RefreshZoomUi();
            Application.AddMessageFilter(this);
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
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add("翻译选中文字", null, delegate { TranslateSelectedText(); });
            mTool.DropDownItems.Add("翻译当前页面", null, delegate { TranslateCurrentPage(); });
            mTool.DropDownItems.Add("翻译 API 配置...", null, delegate { ShowTranslationConfig(); });
            
            var miDarkMode = new ToolStripMenuItem("暗色模式");
            miDarkMode.CheckOnClick = true;
            miDarkMode.Checked = DarkModeManager.IsEnabled;
            miDarkMode.ShortcutKeys = (Keys)(Keys.Control | Keys.Shift | Keys.D);
            miDarkMode.Click += delegate(object s, EventArgs e)
            {
                var mi = s as ToolStripMenuItem;
                if (mi == null) return;
                DarkModeManager.IsEnabled = mi.Checked;
            };
            mTool.DropDownItems.Add(miDarkMode);
            mTool.DropDownItems.Add("设为默认浏览器...", null, delegate { SetDefaultBrowser(); });
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add("关于 LoongBrowser", null, delegate { ShowAbout(); });

            var mView = BuildViewMenu();

            _menu.Items.Add(mBookmark);
            _menu.Items.Add(mDownload);
            _menu.Items.Add(mHistory);
            _menu.Items.Add(mView);
            _menu.Items.Add(mTool);
        }

        /// <summary>「查看」菜单：放大 / 缩小 / 实际大小 + 预设档位列表</summary>
        private ToolStripMenuItem BuildViewMenu()
        {
            var mView = new ToolStripMenuItem("查看(&V)");

            _miZoomIn = new ToolStripMenuItem("放大");
            _miZoomIn.ShortcutKeyDisplayString = "Ctrl + +";   // 只做展示，实际拦截在消息过滤器里
            _miZoomIn.Click += delegate { ZoomIn(); };
            mView.DropDownItems.Add(_miZoomIn);

            _miZoomOut = new ToolStripMenuItem("缩小");
            _miZoomOut.ShortcutKeyDisplayString = "Ctrl + -";
            _miZoomOut.Click += delegate { ZoomOut(); };
            mView.DropDownItems.Add(_miZoomOut);

            _miZoomReset = new ToolStripMenuItem("实际大小（100%）");
            _miZoomReset.ShortcutKeyDisplayString = "Ctrl + 0";
            _miZoomReset.Click += delegate { ResetZoom(); };
            mView.DropDownItems.Add(_miZoomReset);

            mView.DropDownItems.Add(new ToolStripSeparator());

            _miZoomList = new ToolStripMenuItem("缩放（100%）");
            _miZoomList.DropDownItems.AddRange(BuildZoomPresetItems());
            _miZoomList.DropDownOpening += delegate { RefreshZoomUi(); };
            mView.DropDownItems.Add(_miZoomList);

            mView.DropDownOpening += delegate { RefreshZoomUi(); };
            return mView;
        }

        /// <summary>生成预设档位菜单项（「查看」菜单与工具栏下拉各用一份，Tag 存比例值）</summary>
        private ToolStripMenuItem[] BuildZoomPresetItems()
        {
            var items = new List<ToolStripMenuItem>();
            foreach (double p in ZoomManager.Presets)
            {
                var mi = new ToolStripMenuItem(ZoomManager.Percent(p));
                mi.Tag = (object)p;
                mi.Click += delegate(object s, EventArgs e)
                {
                    var item = s as ToolStripMenuItem;
                    if (item == null || !(item.Tag is double)) return;
                    SetZoom((double)item.Tag);
                };
                items.Add(mi);
            }
            return items.ToArray();
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
            _addressBox.Width = 520;
            _addressBox.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == (char)13)
                {
                    e.Handled = true;
                    NavigateFromAddress();
                }
            };

            var btnCopy = new ToolStripButton("复制");
            btnCopy.ToolTipText = "复制地址栏内容到剪贴板";
            btnCopy.Click += delegate
            {
                string text = _addressBox.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    try { Clipboard.SetText(text); } catch (Exception) { }
                }
            };

            var btnPaste = new ToolStripButton("粘贴");
            btnPaste.ToolTipText = "从剪贴板粘贴到地址栏";
            btnPaste.Click += delegate
            {
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip))
                    {
                        _addressBox.Text = clip;
                        _addressBox.Focus();
                        _addressBox.SelectAll();
                    }
                }
                catch (Exception) { }
            };

            var btnNewTab = new ToolStripButton("＋新标签");
            btnNewTab.Click += delegate { _tabMgr.NewTab(null); };

            var btnCloseTab = new ToolStripButton("✕关闭");
            btnCloseTab.Click += delegate { _tabMgr.CloseCurrent(); };

            var btnStar = new ToolStripButton("☆ 收藏");
            btnStar.Click += delegate { AddBookmark(); };

            var btnTranslate = new ToolStripButton("翻译");
            btnTranslate.ToolTipText = "翻译选中文字或当前页面";
            btnTranslate.Click += delegate { TranslateSelectedText(); };

            _toolbar.Items.Add(btnBack);
            _toolbar.Items.Add(btnForward);
            _toolbar.Items.Add(btnRefresh);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_addressBox);
            _toolbar.Items.Add(btnCopy);
            _toolbar.Items.Add(btnPaste);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnStar);
            _toolbar.Items.Add(btnTranslate);
            _toolbar.Items.Add(new ToolStripSeparator());

            _zoomButton = new ToolStripDropDownButton();
            _zoomButton.ToolTipText = "网页缩放 50%~200%（Ctrl + 加/减键，或 Ctrl + 滚轮）";
            _zoomButton.DropDownItems.AddRange(BuildZoomPresetItems());
            _zoomButton.DropDownOpening += delegate { RefreshZoomUi(); };
            _toolbar.Items.Add(_zoomButton);

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

            if (_zoomButton != null) _zoomButton.Text = pct;
            if (_miZoomList != null) _miZoomList.Text = "缩放（" + pct + "）";

            // 到达 50% / 200% 边界时置灰对应的步进命令（预设档位仍可直接点选）
            if (_miZoomIn != null) _miZoomIn.Enabled = !ZoomManager.AtMax(cur);
            if (_miZoomOut != null) _miZoomOut.Enabled = !ZoomManager.AtMin(cur);

            CheckZoomPreset(_miZoomList, cur);
            CheckZoomPreset(_zoomButton, cur);
        }

        /// <summary>按当前比例勾中预设档位（同一档才算选中，按 1% 比较）</summary>
        private static void CheckZoomPreset(ToolStripDropDownItem owner, double current)
        {
            if (owner == null) return;
            foreach (ToolStripItem item in owner.DropDownItems)
            {
                var mi = item as ToolStripMenuItem;
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

        // ---------- 暗色模式 ----------

        /// <summary>暗色模式切换时更新界面样式</summary>
        private void OnDarkModeChanged(bool enabled)
        {
            DarkModeManager.ApplyToForm(this);
            ApplyDarkModeToToolbar();
            // 更新工具菜单中的暗色模式勾选状态
            UpdateDarkModeMenuItem();
        }

        /// <summary>更新工具菜单中暗色模式的勾选状态</summary>
        private void UpdateDarkModeMenuItem()
        {
            if (_menu == null) return;
            foreach (ToolStripItem item in _menu.Items)
            {
                ToolStripMenuItem toolMenu = item as ToolStripMenuItem;
                if (toolMenu != null && toolMenu.Text.Contains("工具"))
                {
                    foreach (ToolStripItem subItem in toolMenu.DropDownItems)
                    {
                        ToolStripMenuItem mi = subItem as ToolStripMenuItem;
                        if (mi != null && mi.Text.Contains("暗色模式"))
                        {
                            mi.Checked = DarkModeManager.IsEnabled;
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>将暗色模式样式应用到工具栏</summary>
        private void ApplyDarkModeToToolbar()
        {
            if (_toolbar == null) return;
            bool dark = DarkModeManager.IsEnabled;

            _toolbar.BackColor = dark ? DarkModeManager.ToolStripBackColor : SystemColors.Control;
            _toolbar.ForeColor = dark ? DarkModeManager.ControlForeColor : SystemColors.ControlText;

            foreach (ToolStripItem item in _toolbar.Items)
            {
                item.BackColor = dark ? DarkModeManager.ToolStripBackColor : SystemColors.Control;
                item.ForeColor = dark ? DarkModeManager.ControlForeColor : SystemColors.ControlText;
            }

            // 地址栏特殊处理
            if (_addressBox != null)
            {
                _addressBox.BackColor = dark ? DarkModeManager.ControlBackColor : SystemColors.Window;
                _addressBox.ForeColor = dark ? DarkModeManager.ControlForeColor : SystemColors.WindowText;
            }
        }
    }
}
