// BrowserCleaner.cs —— 缓存清理模块：调用 WebView2 官方 ClearBrowsingData API
using System;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LoongBrowser
{
    public static class BrowserCleaner
    {
        /// <summary>
        /// 清理指定类型的浏览数据。
        /// kinds 可为 CoreWebView2BrowsingDataKinds.DiskCache / History / Cookies 等的组合。
        /// </summary>
        public static async void Clear(Form owner, Func<CoreWebView2> coreGetter,
            CoreWebView2BrowsingDataKinds kinds, string description)
        {
            CoreWebView2 core = null;
            try { core = coreGetter(); } catch (Exception) { }
            if (core == null)
            {
                MessageBox.Show(owner, "暂无可清理的浏览会话，请先打开一个网页。", "提示");
                return;
            }

            var dr = MessageBox.Show(owner, "确定要" + description + "吗？", "确认清理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            try
            {
                await core.Profile.ClearBrowsingDataAsync(kinds);
                MessageBox.Show(owner, description + "完成。", "提示");
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "清理失败：" + ex.Message, "错误");
            }
        }
    }
}
