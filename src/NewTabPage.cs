// NewTabPage.cs —— 新标签页：应用自己生成的"书签墙"
//
// 实现方式：拼 HTML 后用 CoreWebView2.NavigateToString 注入，而不是导航到本地文件。
//   · 不需要网络即可渲染；图标已缓存时以 data: URI 内联，离线也能显示
//   · 拿不到合法图标的站点统一显示内置的空白图标（NewTabPage.UseLetterFallback=true 可改回首字母色块）
//   · 页面地址保持 about:blank，不会把 file:///... 暴露到地址栏
//   · 点击书签通过 chrome.webview.postMessage 回传宿主，由宿主执行 Navigate，
//     因此 file:// 等本地书签也不会被内核的"禁止网页访问本地资源"规则挡住
using System;
using System.Collections.Generic;
using System.Text;

namespace LoongBrowser
{
    public static class NewTabPage
    {
        /// <summary>新标签页是否展示书签墙（工具菜单可切换；关闭即回到空白页）</summary>
        public static bool Enabled = true;

        /// <summary>启动窗口（无命令行参数）时打开的页面。按"默认主页=空白页"的既有设定保持空白；</summary>
        public const string HomeUrl = "about:blank";

        /// <summary>启动时是否也显示书签墙（默认 false：只有"＋新标签"才显示，尊重空白主页设定）</summary>
        public static bool UseForStartup = false;

        /// <summary>书签卡片数量上限，超出只渲染前 N 个</summary>
        public const int MaxCards = 500;

        /// <summary>内联图标数量上限：每个图标约 1~3KB，base64 后 ×1.33，防止页面体积失控</summary>
        public const int MaxIcons = 60;

        /// <summary>
        /// 没有合法图标时怎么办：
        ///   false（默认）→ 用统一的空白图标；
        ///   true → 用"首字母色块"（保留旧观感，可作为配置项切换）
        /// </summary>
        public static bool UseLetterFallback = false;

        /// <summary>是否属于"内部页面"（新标签页/空白页）。宿主的消息桥只接受这类页面发来的跳转请求</summary>
        public static bool IsInternalUrl(string url)
        {
            return string.IsNullOrEmpty(url) || url == "about:blank";
        }

        /// <summary>只允许 http / https / file。书签 URL 可能来自地址栏输入，必须挡掉 javascript:/data: 之类的注入</summary>
        public static bool IsSafeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string s = u.Scheme.ToLowerInvariant();
            return s == "http" || s == "https" || s == "file";
        }

        /// <summary>生成新标签页 HTML（书签为空时显示引导页）</summary>
        public static string BuildHtml(IList<BookmarkItem> bookmarks)
        {
            int total = (bookmarks == null) ? 0 : bookmarks.Count;
            var sb = new StringBuilder(24 * 1024);

            // 空白图标全页只编码一次，用 CSS 背景共享；否则每个无图标书签都会复制一份 base64，
            // 书签一多 HTML 体积会成倍膨胀
            string blankUri = UseLetterFallback ? null : FaviconCache.ToDataUri(FaviconCache.DefaultIconFile);

            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">");
            sb.Append("<title>新标签页</title><style>").Append(Css);
            if (blankUri != null)
                sb.Append(".icb{background-image:url('").Append(blankUri).Append("')}");
            sb.Append("</style></head><body>");
            sb.Append("<div class=\"wrap\">");

            int rendered = 0;
            if (total > 0) rendered = CountRenderable(bookmarks);

            sb.Append("<div class=\"hd\"><span class=\"ttl\">书签</span><span class=\"cnt\">")
              .Append(rendered.ToString()).Append(" 个</span></div>");

            if (rendered == 0)
            {
                sb.Append("<div class=\"empty\">还没有书签<br>")
                  .Append("<span>在任意页面点工具栏的「☆ 收藏」即可加入</span></div>");
            }
            else
            {
                sb.Append("<div class=\"grid\">");
                int shown = 0;
                int icons = 0;

                for (int i = 0; i < total && shown < MaxCards; i++)
                {
                    BookmarkItem b = bookmarks[i];
                    if (b == null || !IsSafeUrl(b.Url)) continue;

                    string url = b.Url;
                    string title = (b.Title == null || b.Title.Trim().Length == 0) ? url : b.Title.Trim();

                    // 真实图标：命中缓存且未超内联上限才内联；否则用共享的空白图标（或首字母块）
                    string realIcon = FaviconCache.GetCached(url);
                    string dataUri = null;
                    if (realIcon != null && icons < MaxIcons)
                    {
                        dataUri = FaviconCache.ToDataUri(realIcon);
                        if (dataUri != null) icons++;
                    }
                    shown++;

                    sb.Append("<a class=\"card\" href=\"").Append(Esc(url))
                      .Append("\" data-url=\"").Append(Esc(url))
                      .Append("\" title=\"").Append(Esc(title)).Append("\n").Append(Esc(url)).Append("\">");

                    if (dataUri != null)
                        sb.Append("<img class=\"ic\" alt=\"\" src=\"").Append(dataUri).Append("\">");
                    else if (blankUri != null)
                        sb.Append("<span class=\"ic icb\"></span>");
                    else
                        sb.Append("<span class=\"ph\" style=\"background:").Append(LetterColor(url)).Append("\">")
                          .Append(Esc(FirstChar(title))).Append("</span>");

                    sb.Append("<span class=\"t\">").Append(Esc(title)).Append("</span></a>");
                }
                sb.Append("</div>");
            }

            sb.Append("<div class=\"ft\">在地址栏输入网址或关键词开始浏览</div>");
            sb.Append("</div><script>").Append(Js).Append("</script></body></html>");
            return sb.ToString();
        }

        /// <summary>触发新标签页去补齐缺失图标（宿主在渲染后调用，结果会在图标就绪后回调）</summary>
        public static void RequestMissingIcons(IList<BookmarkItem> bookmarks, Action onAnyIconReady)
        {
            if (bookmarks == null || !FaviconCache.EnableRemoteFetch) return;
            int asked = 0;
            for (int i = 0; i < bookmarks.Count && asked < MaxIcons; i++)
            {
                BookmarkItem b = bookmarks[i];
                if (b == null || !IsSafeUrl(b.Url)) continue;
                if (FaviconCache.GetCached(b.Url) != null) continue;
                asked++;
                FaviconCache.FetchRemote(b.Url, onAnyIconReady);
            }
        }

        private static int CountRenderable(IList<BookmarkItem> bookmarks)
        {
            int n = 0;
            for (int i = 0; i < bookmarks.Count && n < MaxCards; i++)
            {
                BookmarkItem b = bookmarks[i];
                if (b != null && IsSafeUrl(b.Url)) n++;
            }
            return n;
        }

        // ---------- 工具 ----------

        /// <summary>HTML 转义（文本节点与属性值共用）</summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\'') sb.Append("&#39;");
                else if (c < ' ') sb.Append(' ');       // 控制字符会让属性解析出错
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>卡片占位块的首字符（中文取一个字，emoji 取一对代理项）</summary>
        private static string FirstChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "#";
            string t = s.Trim();
            if (t.Length == 0) return "#";
            if (char.IsHighSurrogate(t[0]) && t.Length > 1) return t.Substring(0, 2);
            return t.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>没有图标时的占位色：由 host 哈希得到稳定色相（同一站点颜色不变）</summary>
        private static string LetterColor(string url)
        {
            string key = FaviconCache.HostOf(url);
            if (string.IsNullOrEmpty(key)) key = url;
            int h = 0;
            for (int i = 0; i < key.Length; i++) h = (h * 31 + key[i]) & 0x7fffffff;
            return "hsl(" + (h % 360) + ",46%,50%)";
        }

        // ---------- 静态资源 ----------

        private const string Css = @"
*{box-sizing:border-box}
html,body{margin:0;min-height:100%}
body{font:14px/1.5 'Segoe UI','Microsoft YaHei',system-ui,sans-serif;color:#1f2329;background:#f6f7f9}
.wrap{max-width:1080px;margin:0 auto;padding:28px 24px 40px}
.hd{display:flex;align-items:baseline;gap:10px;margin:0 2px 16px}
.ttl{font-size:18px;font-weight:600}
.cnt{font-size:12px;color:#8b93a1}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(196px,1fr));gap:10px}
.card{display:flex;align-items:center;gap:10px;padding:10px 12px;background:#fff;border:1px solid #e6e8eb;border-radius:10px;text-decoration:none;color:#1f2329;transition:box-shadow .15s,transform .15s,border-color .15s}
.card:hover{border-color:#c9d2dd;box-shadow:0 2px 10px rgba(20,30,50,.08);transform:translateY(-1px)}
.card:focus{outline:2px solid #3b82f6;outline-offset:1px}
.ic{width:32px;height:32px;flex:0 0 32px;border-radius:6px;object-fit:contain}
.icb{background-repeat:no-repeat;background-position:center;background-size:contain}
.ph{width:32px;height:32px;flex:0 0 32px;border-radius:6px;color:#fff;font-weight:600;font-size:15px;display:flex;align-items:center;justify-content:center}
.t{flex:1 1 auto;min-width:0;font-size:13px;line-height:1.35;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;word-break:break-all}
.empty{margin:64px auto;max-width:420px;text-align:center;color:#5b6472;font-size:15px;line-height:2.1}
.empty span{font-size:12.5px;color:#98a1af}
.ft{margin-top:34px;text-align:center;font-size:12px;color:#a5adb9}
@media (prefers-color-scheme:dark){
body{color:#e6e8eb;background:#1c1f24}
.card{background:#25292f;border-color:#333941;color:#e6e8eb}
.card:hover{border-color:#48505a;box-shadow:0 2px 10px rgba(0,0,0,.35)}
.ph,.ic{background:transparent}
.cnt{color:#8b93a1}
.empty{color:#aab2bd}
.ft{color:#7c8590}
}
";

        private const string Js = @"
(function(){
  document.addEventListener('click',function(e){
    var el=e.target;
    while(el&&el.tagName!=='A')el=el.parentNode;
    if(!el||el.tagName!=='A')return;
    var u=el.getAttribute('data-url');
    if(!u)return;
    e.preventDefault();
    try{window.chrome.webview.postMessage(u);}catch(err){location.href=u;}
  },true);
})();
";
    }
}
