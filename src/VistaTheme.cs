// VistaTheme.cs —— Vista 风格的配色、几何与绘制工具
//
// 玻璃不染色（见 docs/adr/0005-no-glass-tinting.md），Vista 的辨识度由这里的
// 高光边、圆角与冷蓝点缀承担。所有"玻璃区域"一律不绘制背景：本类只提供服务，
// 不在任何控件上铺不透明底色。
//
// 重要限制：GDI 的 SolidBrush 忽略 alpha，无法在半透明层上做真正的混色。
// 因此凡是要透出 Acrylic 的区域，做法是「整个不画」，而不是「画半透明」。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>Vista 风格的配色与几何常量</summary>
    public static class VistaTheme
    {
        // ---------- 几何 ----------
        /// <summary>自绘标题栏高度</summary>
        public const int TitleBarHeight = 30;
        /// <summary>菜单栏高度</summary>
        public const int MenuBarHeight = 26;
        /// <summary>工具栏高度</summary>
        public const int ToolBarHeight = 36;
        /// <summary>统一圆角半径</summary>
        public const int Radius = 8;
        /// <summary>标题栏按钮宽度</summary>
        public const int CaptionButtonWidth = 46;

        // ---------- 前景 ----------
        /// <summary>主文字色（纯黑；用户 2026-10 指定）</summary>
        public static readonly Color Text = Color.Black;
        /// <summary>次要文字色。页签已统一纯黑，当前无引用；保留以备以后恢复灰阶层级。</summary>
        public static readonly Color TextMuted = Color.FromArgb(92, 105, 120);
        /// <summary>禁用文字色</summary>
        public static readonly Color TextDisabled = Color.FromArgb(160, 170, 182);

        // ---------- 高光与描边（Vista 的辨识度所在） ----------
        /// <summary>顶部 1px 白色高光</summary>
        public static readonly Color HighlightTop = Color.FromArgb(255, 255, 255);
        /// <summary>底部 1px 深色阴影</summary>
        public static readonly Color ShadowBottom = Color.FromArgb(120, 132, 146);
        /// <summary>冷蓝点缀（选中标签描边）</summary>
        public static readonly Color AccentBlue = Color.FromArgb(122, 168, 216);
        /// <summary>冷蓝高光（悬停）</summary>
        public static readonly Color AccentBlueLight = Color.FromArgb(206, 228, 248);

        // ---------- 不透明控件（地址栏胶囊、选中标签） ----------
        /// <summary>选中标签 / 地址栏的底色</summary>
        public static readonly Color SurfaceWhite = Color.FromArgb(252, 252, 253);
        /// <summary>未选中标签的底色</summary>
        public static readonly Color SurfaceDim = Color.FromArgb(238, 242, 247);
        /// <summary>控件边框</summary>
        public static readonly Color SurfaceBorder = Color.FromArgb(160, 176, 194);

        // ---------- 标题栏按钮 ----------
        /// <summary>关闭按钮悬停底色</summary>
        public static readonly Color CloseHover = Color.FromArgb(214, 88, 76);
        /// <summary>最小化/最大化悬停底色</summary>
        public static readonly Color CaptionHover = Color.FromArgb(206, 228, 248);
        /// <summary>标题栏按钮符号色</summary>
        public static readonly Color CaptionGlyph = Color.FromArgb(38, 50, 64);

        // ---------- 字体 ----------
        // 使用系统自带的微软雅黑：不内嵌字体文件，exe 保持轻量，也不涉及字体分发授权。
        // WinForms 的 Font 只认单一字体族，没有 CSS 那样的 fallback 链，
        // 这里按"雅黑 UI → 雅黑 → Segoe UI"的顺序取第一个可用的族。
        private static readonly string[] Families = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };

        private static Font _ui;
        private static Font _title;

        /// <summary>常规界面字体（10pt）</summary>
        public static Font UIFont
        {
            get
            {
                if (_ui == null) _ui = MakeFont(10f, FontStyle.Regular);
                return _ui;
            }
        }

        /// <summary>标题字体（10.5pt）</summary>
        public static Font TitleFont
        {
            get
            {
                if (_title == null) _title = MakeFont(10.5f, FontStyle.Regular);
                return _title;
            }
        }

        private static Font MakeFont(float size, FontStyle style)
        {
            for (int i = 0; i < Families.Length; i++)
            {
                try { return new Font(Families[i], size, style, GraphicsUnit.Point); }
                catch (Exception) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }

        // ---------- 绘制工具 ----------

        /// <summary>构造圆角矩形路径</summary>
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                p.CloseFigure();
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>
        /// 画一条 1px 的顶部高光 + 底部阴影，模拟 Vista 玻璃的边缘。
        /// 这是玻璃区域唯一会画的东西 —— 不铺底色，中间的 Acrylic 原样透出。
        /// </summary>
        public static void DrawEdgeHighlight(Graphics g, Rectangle bounds)
        {
            using (var pen = new Pen(HighlightTop, 1f))
                g.DrawLine(pen, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
            using (var pen = new Pen(ShadowBottom, 1f))
                g.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        }

        /// <summary>画一个不透明的圆角面板（地址栏胶囊、选中标签等）</summary>
        public static void DrawSurface(Graphics g, Rectangle bounds, Color fill, Color border, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = Rounded(bounds, radius))
            {
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);
                if (border != Color.Empty)
                {
                    using (var p = new Pen(border, 1f))
                        g.DrawPath(p, path);
                }
            }
        }

        /// <summary>按可用宽度做省略号截断</summary>
        /// <remarks>
        /// 度量必须用 g.MeasureString：外壳文字已改由 DrawChromeText（GDI+）绘制，
        /// 若这里仍用 TextRenderer.MeasureText（GDI），两套度量不一致会让省略号错位。
        /// </remarks>
        public static string Ellipsize(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = StripAmpersands(text);
            if (MeasureWidth(g, text, font) <= maxWidth) return text;

            string ell = "...";
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                string cand = text.Substring(0, mid) + ell;
                if (MeasureWidth(g, cand, font) <= maxWidth) lo = mid; else hi = mid - 1;
            }
            return lo <= 0 ? ell : text.Substring(0, lo) + ell;
        }

        /// <summary>用 GDI+ 度量单行文字宽度（与 DrawChromeText 的绘制口径一致）</summary>
        public static int MeasureWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (int)Math.Ceiling(g.MeasureString(StripAmpersands(text), font).Width);
        }

        /// <summary>
        /// 去掉 WinForms 加速键标记 "&amp;"（按 TextRenderer 的 NoPrefix 语义）。
        /// "&amp;&amp;" 表示一个字面量的 &amp;，保留一个。
        /// </summary>
        public static string StripAmpersands(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '&' && i + 1 < text.Length)
                {
                    if (text[i + 1] == '&') { sb.Append('&'); i++; }
                    // 单个 & 是加速键标记，丢弃
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------- 外壳文字（微软雅黑 + 纯黑） ----------

        /// <summary>
        /// 外壳文字的统一绘制入口。
        /// </summary>
        /// <remarks>
        /// 用户 2026-10 决定：不画文字阴影，只保留纯黑字。阴影曾按"玻璃上浅、白底上深"
        /// 做成双极性高光，但实测在深色壁纸下仍救不回可读性，且观感偏脏，故整体移除。
        /// 这里仍保留统一入口而不直接调 DrawString：GDI+ 的字符串绘制需要
        /// TextFormatFlags→StringFormat 的映射、& 加速键前缀剥离、TextRenderingHint 设置，
        /// 集中一处免得每个绘制点各写一遍。
        /// </remarks>
        public static void DrawChromeText(Graphics g, string text, Font font, Rectangle bounds,
                                          Color color, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(text) || font == null) return;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // GDI+ 的 DrawString 不认 & 加速键前缀：原先 TextRenderer + NoPrefix 是
            // "原样显示 &"，换成 DrawString 后 & 会直接画出来（如"书签(&B)"）。
            // 外壳不显示加速键下划线，因此按 NoPrefix 语义把 & 标记剥掉。
            if ((flags & TextFormatFlags.NoPrefix) != 0) text = StripAmpersands(text);

            TextRenderingHint oldHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            try
            {
                using (var format = BuildFormat(flags))
                using (var brush = new SolidBrush(color))
                {
                    g.DrawString(text, font, brush, bounds, format);
                }
            }
            finally
            {
                g.TextRenderingHint = oldHint;
            }
        }

        /// <summary>把 TextFormatFlags 映射成 GDI+ 的 StringFormat（只覆盖外壳用到的几个）</summary>
        private static StringFormat BuildFormat(TextFormatFlags flags)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags |= StringFormatFlags.NoWrap;

            // 水平对齐
            if ((flags & TextFormatFlags.HorizontalCenter) != 0) f.Alignment = StringAlignment.Center;
            else if ((flags & TextFormatFlags.Right) != 0) f.Alignment = StringAlignment.Far;
            else f.Alignment = StringAlignment.Near;

            // 垂直对齐
            if ((flags & TextFormatFlags.VerticalCenter) != 0) f.LineAlignment = StringAlignment.Center;
            else if ((flags & TextFormatFlags.Bottom) != 0) f.LineAlignment = StringAlignment.Far;
            else f.LineAlignment = StringAlignment.Near;

            // 省略号
            if ((flags & TextFormatFlags.EndEllipsis) != 0) f.Trimming = StringTrimming.EllipsisCharacter;
            else if ((flags & TextFormatFlags.PathEllipsis) != 0) f.Trimming = StringTrimming.EllipsisPath;
            else if ((flags & TextFormatFlags.WordEllipsis) != 0) f.Trimming = StringTrimming.EllipsisWord;
            else f.Trimming = StringTrimming.None;

            return f;
        }
    }
}
