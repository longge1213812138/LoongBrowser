// TabStrip.cs —— 标签页外观：更大的标题字号、标签之间留出明显间隔、所有标签统一尺寸
//
// 标准 TabControl 的外观参数很有限（改不了标签之间的空隙），所以用 OwnerDrawFixed：
//   · 固定 ItemSize → 每个标签宽度完全一致（TabSizeMode.Fixed）
//   · 在标签矩形内再内缩一圈 → 形成标签之间的可见缝隙
//   · 自绘圆角卡片：选中=白底深色字，未选中=浅灰底灰字
//   · 超出宽度用省略号，完整标题通过悬停提示（TabControl.ShowToolTips + TabPage.ToolTipText）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class TabStrip
    {
        /// <summary>单个标签的宽度（所有标签统一）</summary>
        public const int TabWidth = 210;

        /// <summary>标签条的高度（比默认高，配合更大字号）</summary>
        public const int TabHeight = 40;

        /// <summary>标签之间的水平间隔</summary>
        public const int GapX = 9;

        /// <summary>标签与标签条顶部的距离</summary>
        public const int GapTop = 6;

        /// <summary>标签底部与内容区之间保留的空隙</summary>
        public const int GapBottom = 3;

        /// <summary>标题字号（默认 9pt，这里调大）</summary>
        public const float TitleSize = 10.5f;

        private static readonly Font TitleFont = MakeFont(TitleSize);

        private static readonly Color SelectedFill = Color.White;
        private static readonly Color SelectedBorder = Color.FromArgb(148, 163, 184);
        private static readonly Color SelectedText = Color.FromArgb(20, 23, 27);
        private static readonly Color NormalFill = Color.FromArgb(247, 249, 251);
        private static readonly Color NormalBorder = Color.FromArgb(211, 218, 227);
        private static readonly Color NormalText = Color.FromArgb(90, 98, 112);
        private static readonly Color StripBack = Color.FromArgb(233, 237, 242);

        /// <summary>标题字体（供测试/外部查询）</summary>
        public static Font TabTitleFont { get { return TitleFont; } }

        /// <summary>把标签条设成统一尺寸 + 更大字号 + 自绘样式</summary>
        public static void Style(TabControl tabs)
        {
            if (tabs == null) return;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.Multiline = false;                  // 单行，超出用滚动箭头
            tabs.ShowToolTips = true;                // 完整标题靠悬停提示
            tabs.ItemSize = new Size(TabWidth, TabHeight);
            tabs.Padding = new Point(0, 0);          // 间隔由自绘负责
            tabs.Font = TitleFont;
            try { tabs.BackColor = StripBack; } catch (Exception) { }   // 标签条底色
            tabs.DrawItem += DrawItem;
        }

        /// <summary>自绘单个标签</summary>
        private static void DrawItem(object sender, DrawItemEventArgs e)
        {
            var tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;

            bool selected = (e.Index == tabs.SelectedIndex);

            // 在标签矩形内内缩：左右留 GapX、上方留 GapTop、下方留 GapBottom
            var r = new Rectangle(
                e.Bounds.X + GapX / 2,
                e.Bounds.Y + GapTop,
                Math.Max(8, e.Bounds.Width - GapX),
                Math.Max(8, e.Bounds.Height - GapTop - GapBottom));

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = Rounded(r, 7))
            {
                using (var brush = new SolidBrush(selected ? SelectedFill : NormalFill))
                    g.FillPath(brush, path);
                using (var pen = new Pen(selected ? SelectedBorder : NormalBorder, 1f))
                    g.DrawPath(pen, path);
            }

            // 标题：左右各留 12px，多余部分省略号
            string text = tabs.TabPages[e.Index].Text;
            if (string.IsNullOrEmpty(text)) text = "新标签页";
            var textRect = new Rectangle(r.X + 12, r.Y + 1, Math.Max(8, r.Width - 22), r.Height - 2);

            TextRenderer.DrawText(g, text, TitleFont, textRect,
                selected ? SelectedText : NormalText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>标签条空白区域的底色（由 TabControl 的 BackColor 决定）</summary>
        public static Color Background { get { return StripBack; } }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Font MakeFont(float size)
        {
            string[] families = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            for (int i = 0; i < families.Length; i++)
            {
                try { return new Font(families[i], size, FontStyle.Regular, GraphicsUnit.Point); }
                catch (Exception) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }
}
