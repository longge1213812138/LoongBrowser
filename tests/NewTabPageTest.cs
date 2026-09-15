// NewTabPageTest.cs —— 新标签页（书签墙）生成的单元测试
// 直接调用真实的 NewTabPage / FaviconCache，不复制逻辑。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace LoongBrowser
{
    public static class NewTabPageTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();
        private static string _tmpIcon;     // 测试期间塞进缓存目录的图标，结束要删掉
        private static readonly List<string> _extraFiles = new List<string>();

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _sb.AppendLine("=== 新标签页（书签墙）单元测试 ===");
            _sb.AppendLine();

            _sb.AppendLine("-- A. URL 安全判定（防 javascript: 注入）--");
            Safe("A1 https", "https://example.com/a", true);
            Safe("A2 http", "http://example.com/a", true);
            Safe("A3 file", "file:///C:/a.html", true);
            Safe("A4 javascript:", "javascript:alert(1)", false);
            Safe("A5 data:", "data:text/html,<script>alert(1)</script>", false);
            Safe("A6 vbscript:", "vbscript:msgbox(1)", false);
            Safe("A7 ftp", "ftp://example.com/x", false);
            Safe("A8 空", "", false);
            Safe("A9 非 URL", "hello world", false);

            _sb.AppendLine();
            _sb.AppendLine("-- B. 内部页面判定 --");
            Internal("B1 空串", "", true);
            Internal("B2 about:blank", "about:blank", true);
            Internal("B3 真实站点", "https://example.com", false);

            _sb.AppendLine();
            _sb.AppendLine("-- C. 图标缓存键（host 归一）--");
            Host("C1 大小写归一", "https://WWW.Bing.COM/x", "www.bing.com");
            Host("C2 带端口", "https://example.com:8443/x", "example.com");
            Host("C3 file 无图标", "file:///C:/a.html", null);
            Host("C4 about 无图标", "about:blank", null);
            Host("C5 非法", "not a url", null);

            _sb.AppendLine();
            _sb.AppendLine("-- D. 页面生成 --");

            // D1 空书签 → 引导页
            string empty = NewTabPage.BuildHtml(new List<BookmarkItem>());
            Contains("D1 空书签显示引导语", empty, "还没有书签");
            NotContains("D2 空书签不渲染卡片", empty, "class=\"card\"");
            Contains("D3 引导页提示收藏入口", empty, "☆ 收藏");

            // 准备一个真实可用的图标（放进缓存目录，键为 newtab-test.invalid —— .invalid 是保留域名，不会撞用户真实数据）
            _tmpIcon = FaviconCache.IconPath("https://newtab-test.invalid/");
            bool iconReady = WriteTestIcon(_tmpIcon);

            var list = new List<BookmarkItem>();
            list.Add(new BookmarkItem { Title = "Example 站点", Url = "https://newtab-test.invalid/page" }); // 有图标
            list.Add(new BookmarkItem { Title = "本地页面", Url = "file:///C:/docs/a.html" });              // file，无图标
            list.Add(new BookmarkItem { Title = "<b>标题要转义</b>", Url = "https://noicon.invalid/x?y=1&z=2" });
            list.Add(new BookmarkItem { Title = "", Url = "https://example.org/path" });                   // 标题空 → 用 URL
            list.Add(new BookmarkItem { Title = "危险书签", Url = "javascript:alert(document.cookie)" });    // 必须被过滤

            string html = NewTabPage.BuildHtml(list);

            Contains("D4 书签标题上屏", html, "Example 站点", "本地页面");
            Contains("D5 标题被 HTML 转义", html, "&lt;b&gt;标题要转义&lt;/b&gt;");
            NotContains("D6 危险书签不上屏", html, "javascript:");
            Contains("D7 URL 作为后备标题", html, "https://example.org/path");
            Contains("D8 属性中的 & 被转义", html, "y=1&amp;z=2");
            NotContains("D9 属性不得出现裸引号截断", html, "y=1&z=2\"");
            Contains("D10 file:// 书签可以上屏", html, "file:///C:/docs/a.html");
            Contains("D11 有图标的用 img", html, "class=\"ic\"");
            Contains("D12 无图标的用共享空白图标", html, "class=\"ic icb\"");
            NotContains("D12b 默认不再出现首字母色块", html, "class=\"ph\"");
            Contains("D13 点击桥脚本存在", html, "chrome.webview.postMessage");
            Contains("D14 CSP 限制外部资源", html, "Content-Security-Policy");
            // 计数只统计可上屏的书签（5 个里过滤掉 1 个危险书签）
            Contains("D15 计数与可上屏数量一致", html, "4 个");
            if (iconReady) Contains("D16 图标以 data URI 内联", html, "data:image/png;base64,");
            else Info("D16 跳过（无法写入测试图标）");

            // D17 开关：改用首字母色块
            NewTabPage.UseLetterFallback = true;
            string html2 = NewTabPage.BuildHtml(list);
            Contains("D17 开关打开后回到首字母色块", html2, "class=\"ph\"");
            NotContains("D17b 此时不再输出空白图标的 CSS 规则", html2, "background-image:url(");
            NewTabPage.UseLetterFallback = false;

            // D18 空白图标文件
            string blank = FaviconCache.DefaultIconFile;
            Judge("D18 空白图标已生成", blank != null && File.Exists(blank),
                  "路径: " + (blank == null ? "null" : blank));
            if (blank != null && File.Exists(blank))
            {
                try
                {
                    using (var bmp = new Bitmap(blank))
                        Judge("D19 空白图为 " + FaviconCache.IconSize + "×" + FaviconCache.IconSize,
                              bmp.Width == FaviconCache.IconSize && bmp.Height == FaviconCache.IconSize,
                              "实际 " + bmp.Width + "×" + bmp.Height);
                }
                catch (Exception ex) { Judge("D19 空白图可解码", false, ex.Message); }
            }

            // D20 标记"该站点没有图标"
            string noIconUrl = "https://no-icon-probe.invalid/x";
            Judge("D20 标记前无缓存", FaviconCache.GetCached(noIconUrl) == null, "");
            FaviconCache.MarkNoIcon(noIconUrl);
            Judge("D20 MarkNoIcon 后命中缓存",
                  FaviconCache.GetCached(noIconUrl) != null, "路径: " + FaviconCache.IconPath(noIconUrl));
            _extraFiles.Add(FaviconCache.IconPath(noIconUrl));
            _sb.AppendLine();

            _sb.AppendLine("-- E. 数量上限（性能边界）--");
            var many = new List<BookmarkItem>();
            for (int i = 0; i < NewTabPage.MaxCards + 5; i++)
                many.Add(new BookmarkItem { Title = "书签 " + i, Url = "https://site" + i + ".invalid/p" });
            string big = NewTabPage.BuildHtml(many);
            int cards = CountOccurrences(big, "class=\"card\"");
            Judge("E1 卡片数量被 MaxCards(" + NewTabPage.MaxCards + ") 限制", cards == NewTabPage.MaxCards,
                  "实际 " + cards);
            int blankCopies = CountOccurrences(big, "data:image/png;base64,");
            Judge("E2 空白图标整页只内联 1 份（不随书签数膨胀）", blankCopies == 1, "实际 " + blankCopies + " 份");
            Info("E1 生成 HTML 体积: " + (big.Length / 1024) + " KB / " + many.Count + " 个书签");

            Cleanup();
            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        // ---------- 断言 ----------

        private static void Safe(string name, string url, bool expect)
        {
            bool actual = NewTabPage.IsSafeUrl(url);
            Judge(name + " → " + (expect ? "允许" : "拦截"), actual == expect, "输入: " + url);
        }

        private static void Internal(string name, string url, bool expect)
        {
            bool actual = NewTabPage.IsInternalUrl(url);
            Judge(name, actual == expect, "输入: " + url);
        }

        private static void Host(string name, string url, string expect)
        {
            string actual = FaviconCache.HostOf(url);
            bool ok = string.Equals(actual, expect);
            Judge(name, ok, "输入: " + url + " 期望: " + (expect == null ? "null" : expect) +
                  " 实际: " + (actual == null ? "null" : actual));
        }

        private static void Contains(string name, string hay, params string[] needles)
        {
            bool ok = true;
            for (int i = 0; i < needles.Length; i++)
                if (hay.IndexOf(needles[i], StringComparison.Ordinal) < 0) ok = false;
            Judge(name, ok, "应包含: " + string.Join(" | ", needles));
        }

        private static void NotContains(string name, string hay, string needle)
        {
            Judge(name, hay.IndexOf(needle, StringComparison.Ordinal) < 0, "不应包含: " + needle);
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            _sb.AppendLine("       * " + s);
        }

        private static int CountOccurrences(string hay, string needle)
        {
            int n = 0, i = 0;
            while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        // ---------- 测试用图标 ----------

        private static bool WriteTestIcon(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var bmp = new Bitmap(FaviconCache.IconSize, FaviconCache.IconSize))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(255, 30, 120, 220));
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        File.WriteAllBytes(path, ms.ToArray());
                    }
                }
                return File.Exists(path);
            }
            catch (Exception) { return false; }
        }

        private static void Cleanup()
        {
            try { if (!string.IsNullOrEmpty(_tmpIcon) && File.Exists(_tmpIcon)) File.Delete(_tmpIcon); }
            catch (Exception) { }
            for (int i = 0; i < _extraFiles.Count; i++)
            {
                try
                {
                    string p = _extraFiles[i];
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p);
                }
                catch (Exception) { }
            }
        }
    }
}
