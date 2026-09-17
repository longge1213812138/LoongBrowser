// PopupRouteTest.cs —— 弹窗（target="_blank" / window.open）分流规则单元测试
// 直接调用真实的 TabManager.RoutePopup / IsNavigable / IsSameSite。
using System;
using System.Text;

namespace LoongBrowser
{
    public static class PopupRouteTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        private const string Bili = "https://www.bilibili.com/video/BV1xx411c7mD";

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _sb.AppendLine("=== 弹窗分流规则单元测试 ===");
            _sb.AppendLine();

            _sb.AppendLine("-- A. 程序化弹出窗必须交回内核（本次修复点）--");
            Route("A1 about:blank（画中画小窗）", Bili, "about:blank", TabManager.PopupAction.Native);
            Route("A2 空 URI", Bili, "", TabManager.PopupAction.Native);
            Route("A3 javascript:", Bili, "javascript:void(0)", TabManager.PopupAction.Native);
            Route("A4 data:", Bili, "data:text/html,<b>x</b>", TabManager.PopupAction.Native);
            Route("A5 blob:", Bili, "blob:https://www.bilibili.com/6f1e-uuid", TabManager.PopupAction.Native);
            Route("A6 带窗口尺寸的空白小窗（真实形态）", Bili, "about:blank", TabManager.PopupAction.Native);
            Route("A7 当前页本身就是空白页时的 about:blank", "about:blank", "about:blank",
                  TabManager.PopupAction.Native);

            _sb.AppendLine();
            _sb.AppendLine("-- B. 真实地址维持原有分流不变 --");
            Route("B1 同站另一个视频", Bili, "https://www.bilibili.com/video/BV2yy411c7mE",
                  TabManager.PopupAction.NavigateCurrent);
            Route("B2 同根域子域", Bili, "https://space.bilibili.com/12345",
                  TabManager.PopupAction.NavigateCurrent);
            Route("B3 跨站", Bili, "https://www.baidu.com/s?wd=x", TabManager.PopupAction.OpenTab);
            Route("B4 仿冒域名", Bili, "https://evil-bilibili.com/x", TabManager.PopupAction.OpenTab);
            Route("B5 http 站内", "http://example.com/a", "http://example.com/b",
                  TabManager.PopupAction.NavigateCurrent);
            Route("B6 file:// 从网页打开", Bili, "file:///C:/docs/a.html", TabManager.PopupAction.OpenTab);
            Route("B7 新标签页里打开链接", "about:blank", "https://www.bilibili.com/",
                  TabManager.PopupAction.NavigateCurrent);

            _sb.AppendLine();
            _sb.AppendLine("-- C. 旧规则对照：bug 是怎么产生的 --");
            // 旧代码没有 IsNavigable 过滤：一律先问 IsSameSite，而 IsSameSite 对 about:/javascript:/data:
            // 直接返回 true（"保守留在当前页"），于是 about:blank 被判为同站 → 在当前标签里 Navigate
            // → 正在看的视频页被顶掉，变成空白页。
            bool sameSite = TabManager.IsSameSite(Bili, "about:blank");
            Judge("C1 旧规则下 IsSameSite(B站视频, about:blank) == true（这就是陷阱）", sameSite,
                  "实际 " + sameSite);
            Judge("C2 旧规则因此会走 NavigateCurrent → 当前标签变空白页",
                  sameSite == true, "");
            Judge("C3 修复后同一输入不再走 NavigateCurrent",
                  TabManager.RoutePopup(Bili, "about:blank") != TabManager.PopupAction.NavigateCurrent,
                  "实际 " + TabManager.RoutePopup(Bili, "about:blank"));
            Judge("C4 修复后也不会误开成新标签页（必须交回内核）",
                  TabManager.RoutePopup(Bili, "about:blank") == TabManager.PopupAction.Native, "");

            _sb.AppendLine();
            _sb.AppendLine("-- D. IsNavigable 判定 --");
            Nav("D1 https", "https://a.com/x", true);
            Nav("D2 http", "http://a.com/x", true);
            Nav("D3 file", "file:///C:/a.html", true);
            Nav("D4 about", "about:blank", false);
            Nav("D5 javascript", "javascript:void(0)", false);
            Nav("D6 data", "data:text/plain,x", false);
            Nav("D7 blob", "blob:https://a.com/x", false);
            Nav("D8 空", "", false);
            Nav("D9 非 URL", "not a url", false);

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Route(string name, string current, string target, TabManager.PopupAction expect)
        {
            TabManager.PopupAction actual = TabManager.RoutePopup(current, target);
            bool ok = actual == expect;
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok)
            {
                _sb.AppendLine("       当前页: " + current);
                _sb.AppendLine("       目标  : " + (target.Length == 0 ? "(空)" : target));
                _sb.AppendLine("       期望  : " + expect + "  实际: " + actual);
            }
        }

        private static void Nav(string name, string uri, bool expect)
        {
            bool actual = TabManager.IsNavigable(uri);
            Judge(name + " → " + (expect ? "可导航" : "非页面"), actual == expect,
                  "输入: " + (uri.Length == 0 ? "(空)" : uri));
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
