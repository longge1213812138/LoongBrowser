// SnapTest.cs —— Aero Snap 单元测试（纯函数，不开窗口）
//
// 锁住三件事：
//   A. 贴边判定：左右边缘 → 半屏；顶部 → 最大化；中间 → 不吸附
//   B. 半屏矩形精确等于工作区的一半（左右拼起来必须正好铺满，不重不漏）
//   C. 从最大化拖离的还原矩形：宽度恢复、指针落在标题栏上
using System;
using System.Drawing;
using System.Text;

namespace LoongBrowser
{
    public static class SnapTest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            _sb.AppendLine("=== Aero Snap（自实现）单元测试 ===");

            // 2560x1440 屏幕，任务栏 48px
            var work = new Rectangle(0, 0, 2560, 1392);
            int d = SnapController.SnapDistance;

            _sb.AppendLine("-- A. 贴边判定 --");
            Judge("A1 左边缘 → 左半屏",
                  SnapController.ZoneAt(new Point(4, 600), work) == SnapController.Zone.Left, "");
            Judge("A2 右边缘 → 右半屏",
                  SnapController.ZoneAt(new Point(work.Right - 4, 600), work) == SnapController.Zone.Right, "");
            Judge("A3 顶部 → 最大化",
                  SnapController.ZoneAt(new Point(1200, 4), work) == SnapController.Zone.Maximize, "");
            Judge("A4 中间 → 不吸附",
                  SnapController.ZoneAt(new Point(1200, 600), work) == SnapController.Zone.None, "");
            Judge("A5 刚好在阈值上算贴边",
                  SnapController.ZoneAt(new Point(work.Left + d, 600), work) == SnapController.Zone.Left, "");
            Judge("A6 超过阈值一个像素就不算",
                  SnapController.ZoneAt(new Point(work.Left + d + 1, 600), work) == SnapController.Zone.None, "");
            Judge("A7 左上角优先判定为最大化（与系统一致）",
                  SnapController.ZoneAt(new Point(2, 2), work) == SnapController.Zone.Maximize, "");

            _sb.AppendLine();
            _sb.AppendLine("-- B. 半屏矩形 --");
            Rectangle left = SnapController.BoundsFor(SnapController.Zone.Left, work);
            Rectangle right = SnapController.BoundsFor(SnapController.Zone.Right, work);
            Judge("B1 左半屏左沿贴住工作区", left.Left == work.Left, left.ToString());
            Judge("B2 左半屏宽度 = 工作区一半", left.Width == work.Width / 2, left.Width + " vs " + work.Width / 2);
            Judge("B3 右半屏左沿紧接左半屏", right.Left == left.Right, left.ToString() + " / " + right.ToString());
            Judge("B4 左右正好拼满整条工作区（不重不漏）",
                  right.Right == work.Right, right.Right + " vs " + work.Right);
            Judge("B5 半屏高度 = 工作区高度（不盖任务栏）", left.Height == work.Height, left.Height + " vs " + work.Height);
            Judge("B6 半屏顶边不越过任务栏", left.Top == work.Top, left.Top + " vs " + work.Top);
            Judge("B7 奇数宽度也不留缝",
                  SnapController.BoundsFor(SnapController.Zone.Left, new Rectangle(0, 0, 2561, 1000)).Right ==
                  SnapController.BoundsFor(SnapController.Zone.Right, new Rectangle(0, 0, 2561, 1000)).Left, "");
            Judge("B8 最大化矩形 = 工作区",
                  SnapController.BoundsFor(SnapController.Zone.Maximize, work) == work, "");

            _sb.AppendLine();
            _sb.AppendLine("-- C. 从最大化拖离的还原 --");
            var restored = new Rectangle(400, 200, 900, 620);
            var maximized = work;
            Rectangle r = SnapController.RestoreBounds(restored, maximized, new Point(400, 300));
            Judge("C1 还原宽度等于原来的宽度", r.Width == restored.Width, r.ToString());
            Judge("C2 还原高度等于原来的高度", r.Height == restored.Height, r.ToString());
            Judge("C3 指针仍落在还原后的窗口内（水平）", r.Left <= 400 && 400 < r.Right, r.ToString());
            Judge("C4 指针落在标题栏附近（垂直）", Math.Abs(r.Top - 300) <= VistaTheme.TitleBarHeight, r.ToString());

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
