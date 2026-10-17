// TabStrip.cs —— 标签页外观：更大的标题字号、标签之间留出明显间隔、所有标签统一尺寸
//
// 标准 TabControl 的外观参数很有限（改不了标签之间的空隙），所以用 OwnerDrawFixed ：
//   · 固定 ItemSize → 每个标签宽度完全一致（TabSizeMode.Fixed）
//   · 在标签矩形内再内缩一圈 → 形成标签之间的可见缝隙
//   · 自绘圆角卡片：选中=白底深色字，未选中=浅灰底灰字
//   · 超出宽度用省略号，完整标题通过悬停提示（TabControl.ShowToolTips + TabPage.ToolTipText）
//   · 每个标签右侧带关闭按钮（x），点击可关闭标签页
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
        public const float TitleSize = 11.5f;

        /// <summary>关闭按钮尺寸</summary>
        public const int CloseBtnSize = 16;

        /// <summary>关闭按钮右侧边距</summary>
        public const int CloseBtnRightMargin = 6;

        /// <summary>当点击标签的关闭按钮时触发，参数为标签索引</summary>
        public static event Action<int> TabCloseRequested;

        /// <summary>字体族优先顺序：黑体优先（用户 2026-10 指定），雅黑与 Segoe UI 仅作族缺失时的兜底</summary>
        private static readonly string[] Families = new string[] { "SimHei", "Microsoft YaHei UI", "Segoe UI" };

        private static readonly Font TitleFont = MakeFont(TitleSize);

        // ========== 标签条配色（无暗色形态，见 docs/adr/0002） ==========
        // 标签文字两态统一用 VistaTheme.Text（纯黑）：未选中标签不再做灰阶区分。
        // 原 SelectedText / NormalText 两个私有常量因此不再有引用，直接删除以免留下死字段。
        private static readonly Color SelectedFill = Color.White;
        private static readonly Color SelectedBorder = Color.FromArgb(148, 163, 184);
        private static readonly Color NormalFill = Color.FromArgb(247, 249, 251);
        private static readonly Color NormalBorder = Color.FromArgb(211, 218, 227);

        /// <summary>标签条背景色（供外部查询）</summary>
        public static Color Background
        {
            get { return Color.FromArgb(233, 237, 242); }
        }

        /// <summary>标题字体（供测试/外部查询）</summary>
        public static Font TabTitleFont { get { return TitleFont; } }

        /// <summary>
        /// 把 TabControl 的标签头压到最小：只留它当 WebView2 的容器，
        /// 标签的视觉与点击命中交给 GlassChrome 自绘（见 docs/adr/0004）。
        /// 实测 ItemSize 高度设 1 后仍残留约 5px 的标题区，无法完全归零。
        /// </summary>
        public static void HideHeader(TabControl tabs)
        {
            if (tabs == null) return;
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.Multiline = false;
            tabs.ItemSize = new Size(0, 1);
            tabs.Padding = new Point(0, 0);
            try { tabs.BackColor = Background; } catch (Exception) { }
        }

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
            try { tabs.BackColor = Background; } catch (Exception) { }   // 标签条底色
            tabs.DrawItem += DrawItem;
            tabs.MouseDown += Tabs_MouseDown;        // 检测关闭按钮点击
        }

        /// <summary>检测鼠标点击是否在某个标签的关闭按钮上</summary>
        private static void Tabs_MouseDown(object sender, MouseEventArgs e)
        {
            var tabs = sender as TabControl;
            if (tabs == null || e.Button != MouseButtons.Left) return;

            // 遍历所有标签，检查点击位置是否在某个标签的关闭按钮区域内
            for (int i = 0; i < tabs.TabCount; i++)
            {
                Rectangle closeBtnRect = GetCloseButtonRect(tabs, i);
                if (closeBtnRect.Contains(e.Location))
                {
                    // 触发关闭事件
                    if (TabCloseRequested != null)
                        TabCloseRequested(i);
                    return;
                }
            }
        }

        /// <summary>获取指定标签的关闭按钮矩形区域</summary>
        private static Rectangle GetCloseButtonRect(TabControl tabs, int index)
        {
            Rectangle tabRect = tabs.GetTabRect(index);
            int x = tabRect.Right - CloseBtnSize - CloseBtnRightMargin;
            int y = tabRect.Top + (tabRect.Height - CloseBtnSize) / 2;
            return new Rectangle(x, y, CloseBtnSize, CloseBtnSize);
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

            // 标题：左右各留 12px，右侧预留关闭按钮空间
            string text = tabs.TabPages[e.Index].Text;
            if (string.IsNullOrEmpty(text)) text = "新标签页";
            int closeBtnSpace = CloseBtnSize + CloseBtnRightMargin + 2;
            var textRect = new Rectangle(r.X + 12, r.Y + 1, Math.Max(8, r.Width - 12 - closeBtnSpace), r.Height - 2);

            // 选中/未选中两态统一纯黑；卡片是不透明的白底或浅灰底，因此用 LightSurface 极性（深色阴影）。
            // 走 VistaTheme.DrawChromeText 而非 TextRenderer：后者忽略 foreColor 的 alpha，画不出半透明阴影
            // （实测细节见 src/VistaTheme.cs 里 DrawChromeText 的 remarks）。
            VistaTheme.DrawChromeText(g, text, TitleFont, textRect,
                VistaTheme.Text, VistaTheme.ShadowPolarity.LightSurface,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // 绘制关闭按钮（x）
            Rectangle closeBtnRect = GetCloseButtonRect(tabs, e.Index);
            DrawCloseButton(g, closeBtnRect, tabs, selected);
        }

        /// <summary>绘制关闭按钮</summary>
        private static void DrawCloseButton(Graphics g, Rectangle rect, TabControl tabs, bool isTabSelected)
        {
            // 检测鼠标是否悬停在按钮上
            Point mousePos = tabs.PointToClient(Cursor.Position);
            bool isHovered = rect.Contains(mousePos);

            // 悬停时显示圆形背景
            if (isHovered)
            {
                using (var brush = new SolidBrush(Color.FromArgb(200, 220, 230, 240)))
                    g.FillEllipse(brush, rect);
            }

            // 绘制 x 符号
            // 字体与主题保持一致：黑体优先（× 在 SimHei 里同样有字形，这里只影响度量与笔画粗细，
            // 让关闭按钮的字面与标签文字同族，避免同一条标签上出现两种字体气质）。
            // 注意：× 是图标不是标签文字，因此仍用 TextRenderer 直接绘制、不加阴影
            // （灰色阶是"未选中更淡"的图标层级，不是文字层级，用户只要求文字两态统一）。
            string closeText = "\u00D7";  // multiplication sign (x)
            using (var font = new Font(Families[0], 9f, FontStyle.Bold))
            {
                var textSize = TextRenderer.MeasureText(g, closeText, font);
                int textX = rect.X + (rect.Width - textSize.Width) / 2;
                int textY = rect.Y + (rect.Height - textSize.Height) / 2;

                Color textColor;
                if (isHovered)
                    textColor = Color.FromArgb(220, 80, 80);  // 悬停时红色
                else
                    textColor = isTabSelected ? Color.FromArgb(130, 130, 130) : Color.FromArgb(170, 170, 170);

                TextRenderer.DrawText(g, closeText, font, new Rectangle(textX, textY, textSize.Width, textSize.Height), textColor);
            }
        }

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
            for (int i = 0; i < Families.Length; i++)
            {
                try { return new Font(Families[i], size, FontStyle.Regular, GraphicsUnit.Point); }
                catch (Exception) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }
}
