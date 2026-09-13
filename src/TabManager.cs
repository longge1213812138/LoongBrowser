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
    }

    public class TabManager
    {
        private readonly TabControl _tabs;
        private CoreWebView2Environment _env;
        private readonly List<TabInfo> _list = new List<TabInfo>();

        /// <summary>下载记录存储：供下载事件登记</summary>
        public DownloadStore Downloads;

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

        /// <summary>新建标签页。url 为空时打开空白页（默认主页）</summary>
        public async void NewTab(string url)
        {
            var view = new WebView2();
            view.Dock = DockStyle.Fill;
            var page = new TabPage("新标签页");
            page.Controls.Add(view);
            var tab = new TabInfo();
            tab.View = view;
            tab.Page = page;
            tab.PendingUrl = string.IsNullOrEmpty(url) ? "about:blank" : url;

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

            // 新窗口请求 → 在本浏览器的新标签打开
            view.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
                NewTab(e.Uri);
            };

            // 下载请求 → 交给默认下载流程保存，同时登记到下载管理
            view.CoreWebView2.DownloadStarting += delegate(object s, CoreWebView2DownloadStartingEventArgs e)
            {
                if (Downloads != null && e.DownloadOperation != null)
                    Downloads.Track(e.DownloadOperation);
            };
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
