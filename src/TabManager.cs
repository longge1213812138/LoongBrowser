// TabManager.cs —— 多标签页管理：标签创建/切换/关闭 + WebView2 生命周期
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public class TabInfo
    {
        public WebView2 View;
        public TabPage Page;
        public string PendingUrl;
        /// <summary>当前是否显示着"新标签页（书签墙）"，用于书签变化后精准刷新</summary>
        public bool ShowingNewTabPage;
    }

    public class TabManager
    {
        private readonly TabControl _tabs;
        // 进程内共享同一个内核环境：多窗口（右键"在新窗口中打开"）不会争抢用户数据目录
        private static CoreWebView2Environment _env;
        private readonly List<TabInfo> _list = new List<TabInfo>();

        /// <summary>下载记录存储：供下载事件登记</summary>
        public DownloadStore Downloads;

        /// <summary>书签存储：新标签页（书签墙）的数据来源</summary>
        public BookmarkStore Bookmarks;

        /// <summary>注入现成的内核环境（测试或多环境复用时用；正常运行时为 null，由 NewTab 自行创建）</summary>
        public static void SetEnvironment(CoreWebView2Environment env)
        {
            _env = env;
        }

        /// <summary>当前选中标签变化（用于同步地址栏等 UI）</summary>
        public event Action<TabInfo> TabChanged;

        public TabManager(TabControl tabs)
        {
            _tabs = tabs;
            _tabs.SelectedIndexChanged += delegate
            {
                var t = Current;
                if (TabChanged != null) TabChanged(t);
            };
        }

        public TabInfo Current
        {
            get
            {
                foreach (var t in _list)
                    if (t.Page == _tabs.SelectedTab) return t;
                return null;
            }
        }

        public WebView2 ActiveView()
        {
            var t = Current;
            return t != null ? t.View : null;
        }

        public CoreWebView2 ActiveCore()
        {
            var v = ActiveView();
            return (v != null && v.CoreWebView2 != null) ? v.CoreWebView2 : null;
        }

        public string ActiveBrowserVersion()
        {
            if (_env != null) return _env.BrowserVersionString;
            return "";
        }

        /// <summary>
        /// 新建标签页。
        /// url 为空 → 显示"新标签页（书签墙）"（NewTabPage.Enabled 为 false 时回落到空白页）；
        /// 传入具体地址 → 直接导航（启动窗口用 NewTabPage.HomeUrl 保持空白主页）。
        /// </summary>
        public async void NewTab(string url)
        {
            var view = new WebView2();
            view.Dock = DockStyle.Fill;
            var page = new TabPage("新标签页");
            page.Controls.Add(view);
            var tab = new TabInfo();
            tab.View = view;
            tab.Page = page;
            tab.PendingUrl = string.IsNullOrEmpty(url) ? null : url;

            _list.Add(tab);
            _tabs.TabPages.Add(page);
            _tabs.SelectedTab = page;

            if (_env == null)
            {
                try
                {
                    _env = await CoreWebView2Environment.CreateAsync(null, AppPaths.UserDataDir);
                }
                catch (Exception)
                {
                    ShowRuntimeMissing();
                    RemoveTab(tab);
                    return;
                }
            }

            try
            {
                await view.EnsureCoreWebView2Async(_env);
            }
            catch (Exception ex)
            {
                MessageBox.Show("浏览器内核初始化失败：" + ex.Message, "错误");
                RemoveTab(tab);
                return;
            }

            Wire(tab);
            if (tab.PendingUrl != null)
            {
                string u = tab.PendingUrl;
                tab.PendingUrl = null;
                view.CoreWebView2.Navigate(u);
            }
            else
            {
                ShowNewTabPage(tab);
            }
        }

        /// <summary>把标签页渲染成"新标签页（书签墙）"</summary>
        public void ShowNewTabPage(TabInfo tab)
        {
            if (tab == null || tab.View == null) return;
            var core = tab.View.CoreWebView2;
            if (core == null) return;
            tab.ShowingNewTabPage = true;
            if (!NewTabPage.Enabled)
            {
                core.Navigate(NewTabPage.HomeUrl);
                return;
            }
            core.NavigateToString(NewTabPage.BuildHtml(Bookmarks != null ? Bookmarks.Items : null));
            // 缺图标的书签：后台补抓，抓到一个就回来刷新一次页面
            NewTabPage.RequestMissingIcons(Bookmarks != null ? Bookmarks.Items : null, OnIconReady);
        }

        /// <summary>图标刚就绪（后台线程）→ 回 UI 线程刷新新标签页</summary>
        private void OnIconReady()
        {
            try
            {
                if (_tabs != null && _tabs.IsHandleCreated)
                    _tabs.BeginInvoke(new Action(RefreshNewTabPages));
            }
            catch (Exception) { }
        }

        /// <summary>书签或图标变化后，刷新所有正在显示新标签页的标签</summary>
        public void RefreshNewTabPages()
        {
            foreach (var t in _list)
            {
                if (t.ShowingNewTabPage) ShowNewTabPage(t);
            }
        }

        private void Wire(TabInfo tab)
        {
            var view = tab.View;

            view.CoreWebView2.DocumentTitleChanged += delegate
            {
                string title = view.CoreWebView2.DocumentTitle;
                if (string.IsNullOrEmpty(title)) title = "新标签页";
                tab.Page.Text = title.Length > 20 ? title.Substring(0, 20) + "…" : title;
            };

            view.CoreWebView2.SourceChanged += delegate
            {
                if (tab == Current && TabChanged != null) TabChanged(tab);
            };

            // 新窗口请求（target="_blank" / window.open）→ 按主流浏览器规则分流：
            //   同站跳转 → 留在当前标签页内导航（写入浏览历史，前进/后退可用）
            //   跨站链接 → 新开标签页
            view.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
                string target = e.Uri;
                if (string.IsNullOrEmpty(target)) return;

                string current = "";
                try { current = view.CoreWebView2.Source ?? ""; } catch (Exception) { }

                if (IsSameSite(current, target))
                    view.CoreWebView2.Navigate(target);
                else
                    NewTab(target);
            };

            // 下载请求 → 交给默认下载流程保存，同时登记到下载管理
            view.CoreWebView2.DownloadStarting += delegate(object s, CoreWebView2DownloadStartingEventArgs e)
            {
                if (Downloads != null && e.DownloadOperation != null)
                    Downloads.Track(e.DownloadOperation);
            };

            // 右键菜单 → 自定义菜单（修复默认"在新窗口中打开链接"失效问题，
            // 并新增"在新标签页中打开链接"）；可编辑框保留默认菜单（复制/粘贴）
            view.CoreWebView2.ContextMenuRequested += delegate(object s, CoreWebView2ContextMenuRequestedEventArgs e)
            {
                BuildContextMenu(view, e);
            };

            // 离开新标签页/空白页时清除标记（NavigateToString 的地址就是 about:blank，不会误清）
            view.CoreWebView2.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!NewTabPage.IsInternalUrl(e.Uri)) tab.ShowingNewTabPage = false;
            };

            // 站点图标就绪 → 缓存起来给新标签页用（WebView2 官方接口，拿到的是站点声明的图标）
            view.CoreWebView2.FaviconChanged += delegate(object s, object e)
            {
                FaviconCache.CaptureFromWebView(view.CoreWebView2, OnIconReady);
            };

            // 新标签页点击书签 → 页面 postMessage 回传地址，由宿主导航。
            // 这样 file:// 等本地书签不会被内核的"禁止网页访问本地资源"规则挡住。
            view.CoreWebView2.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string cur = "";
                try { cur = view.CoreWebView2.Source ?? ""; } catch (Exception) { }
                if (!NewTabPage.IsInternalUrl(cur)) return;      // 只接受新标签页/空白页发来的请求
                string target = null;
                try { target = e.TryGetWebMessageAsString(); } catch (Exception) { }
                if (string.IsNullOrEmpty(target)) return;
                if (Bookmarks == null || !Bookmarks.Contains(target)) return;  // 只允许跳到已收藏的地址
                view.CoreWebView2.Navigate(target);
            };
        }

        private void BuildContextMenu(WebView2 view, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var target = e.ContextMenuTarget;

            // 输入框等可编辑场景：交给 WebView2 默认菜单（保留复制/粘贴/全选）
            if (target != null && target.IsEditable) return;

            bool isLink = target != null && target.HasLinkUri && !string.IsNullOrEmpty(target.LinkUri);
            bool isImage = target != null && target.HasSourceUri && !string.IsNullOrEmpty(target.SourceUri);
            var items = e.MenuItems;

            // 常规导航项
            if (view.CanGoBack)
                items.Add(CreateItem("后退", delegate { view.GoBack(); }));
            if (view.CanGoForward)
                items.Add(CreateItem("前进", delegate { view.GoForward(); }));
            items.Add(CreateItem("刷新", delegate { view.CoreWebView2.Reload(); }));

            if (isLink)
            {
                items.Add(CreateSeparator());
                items.Add(CreateItem("在新标签页中打开链接", delegate { NewTab(target.LinkUri); }));
                items.Add(CreateItem("在新窗口中打开链接", delegate { Program.OpenNewWindow(MainForm.NormalizeUrl(target.LinkUri)); }));
                items.Add(CreateItem("复制链接地址", delegate
                {
                    try { Clipboard.SetText(target.LinkUri); } catch (Exception) { }
                }));
            }

            if (isImage)
            {
                items.Add(CreateSeparator());
                items.Add(CreateItem("在新标签页中打开图片", delegate { NewTab(target.SourceUri); }));
            }

            if (items.Count == 0) return; // 无可显示项时保留默认菜单
            e.Handled = true;
        }

        private CoreWebView2ContextMenuItem CreateItem(string title, Action action)
        {
            var item = _env.CreateContextMenuItem(title, null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += delegate(object s2, object e2)
            {
                try { action(); } catch (Exception) { }
            };
            return item;
        }

        private CoreWebView2ContextMenuItem CreateSeparator()
        {
            return _env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
        }

        public void NavigateCurrent(string url)
        {
            var t = Current;
            if (t == null) return;
            if (t.View.CoreWebView2 == null)
            {
                // 内核尚未初始化完成，挂起待 Wire 后导航
                t.PendingUrl = url;
                return;
            }
            t.View.CoreWebView2.Navigate(url);
        }

        public void CloseCurrent()
        {
            var t = Current;
            if (t == null) return;
            _list.Remove(t);
            _tabs.TabPages.Remove(t.Page);
            try { t.View.Dispose(); } catch (Exception) { }
            if (_tabs.TabPages.Count == 0) NewTab("about:blank");
            else if (TabChanged != null) TabChanged(Current);
        }

        private void RemoveTab(TabInfo tab)
        {
            _list.Remove(tab);
            _tabs.TabPages.Remove(tab.Page);
            try { tab.View.Dispose(); } catch (Exception) { }
        }

        /// <summary>
        /// 判断目标 URL 是否与当前页面同站（主域相同，忽略 www. 前缀）。
        /// 同站返回 true（留在当前标签导航）；跨站返回 false（新开标签）。
        /// 非法/内部协议（about: 等）一律视为同站，保守留在当前页。
        /// </summary>
        public static bool IsSameSite(string current, string target)
        {
            try
            {
                if (string.IsNullOrEmpty(target)) return true;

                Uri tu;
                if (!Uri.TryCreate(target, UriKind.Absolute, out tu)) return true;
                if (tu.Scheme == "about" || tu.Scheme == "javascript" || tu.Scheme == "data") return true;

                Uri cu;
                if (string.IsNullOrEmpty(current) || !Uri.TryCreate(current, UriKind.Absolute, out cu)) return true;
                if (cu.Host.Length == 0) return true; // 当前是 about:blank 等无主机页面

                return NormHost(cu.Host) == NormHost(tu.Host);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormHost(string host)
        {
            // 取根域（最后两段）：cn.bing.com / www.bing.com / bing.com → bing.com
            if (string.IsNullOrEmpty(host)) return host ?? "";
            string[] parts = host.Split('.');
            if (parts.Length <= 2) return host;
            return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
        }

        private void ShowRuntimeMissing()
        {
            var dr = MessageBox.Show(
                "未检测到 Microsoft WebView2 运行时（Chromium 内核组件）。\n" +
                "Windows 10/11 通常已内置；如缺失，请点击\"是\"前往微软官网下载安装（约 2MB 引导器）。",
                "缺少运行时", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr == DialogResult.Yes)
            {
                try { Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703"); } catch (Exception) { }
            }
        }
    }
}
