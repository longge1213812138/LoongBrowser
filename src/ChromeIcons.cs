// ChromeIcons.cs —— 矢量图标：工具栏按钮用 GraphicsPath 现画，不依赖图片资源
//
// 项目零第三方运行时依赖、无资源文件，且图标要能随玻璃/白底两种背景改色。
// Vista 本身就是矢量渲染，方向一致。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LoongBrowser
{
    /// <summary>工具栏与标题栏用到的矢量图标</summary>
    public static class ChromeIcons
    {
        /// <summary>按名称绘制图标，rect 为可用区域（正方形）</summary>
        public static void Draw(Graphics g, string name, Rectangle rect, Color color)
        {
            if (rect.Width < 6 || rect.Height < 6) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int s = Math.Min(rect.Width, rect.Height);

            using (var pen = new Pen(color, Math.Max(1.4f, s / 9f)))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (name)
                {
                    case "back":
                        g.DrawLine(pen, cx + s * 0.18f, cy - s * 0.24f, cx - s * 0.16f, cy);
                        g.DrawLine(pen, cx - s * 0.16f, cy, cx + s * 0.18f, cy + s * 0.24f);
                        break;

                    case "forward":
                        g.DrawLine(pen, cx - s * 0.18f, cy - s * 0.24f, cx + s * 0.16f, cy);
                        g.DrawLine(pen, cx + s * 0.16f, cy, cx - s * 0.18f, cy + s * 0.24f);
                        break;

                    case "refresh":
                        {
                            // 断口圆弧 + 箭头
                            float r = s * 0.28f;
                            var box = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                            g.DrawArc(pen, box, -50, 280);
                            // 箭头在起点
                            float ax = cx + r * (float)Math.Cos(-50 * Math.PI / 180);
                            float ay = cy + r * (float)Math.Sin(-50 * Math.PI / 180);
                            g.DrawLine(pen, ax, ay - s * 0.13f, ax, ay + s * 0.02f);
                            g.DrawLine(pen, ax, ay - s * 0.13f, ax - s * 0.13f, ay - s * 0.02f);
                        }
                        break;

                    case "stop":
                        using (var p = new Pen(color, Math.Max(1.6f, s / 8f)))
                            g.DrawRectangle(p, cx - s * 0.18f, cy - s * 0.18f, s * 0.36f, s * 0.36f);
                        break;

                    case "star":
                        DrawStar(g, cx, cy, s * 0.32f, color, false);
                        break;

                    case "star-filled":
                        DrawStar(g, cx, cy, s * 0.32f, color, true);
                        break;

                    case "translate":
                        {
                            // "文/A" 简化为两条横线 + 一个 A 形三角
                            int w = (int)(s * 0.26f);
                            g.DrawLine(pen, cx - w, cy - s * 0.18f, cx + w, cy - s * 0.18f);
                            g.DrawLine(pen, cx, cy - s * 0.24f, cx, cy - s * 0.02f);
                            g.DrawLine(pen, cx - w * 0.7f, cy + s * 0.02f, cx - w * 0.7f, cy + s * 0.2f);
                            g.DrawLine(pen, cx - w, cy + s * 0.12f, cx - s * 0.1f, cy + s * 0.12f);
                        }
                        break;

                    case "plus":
                        g.DrawLine(pen, cx - s * 0.18f, cy, cx + s * 0.18f, cy);
                        g.DrawLine(pen, cx, cy - s * 0.18f, cx, cy + s * 0.18f);
                        break;

                    case "close":
                        g.DrawLine(pen, cx - s * 0.16f, cy - s * 0.16f, cx + s * 0.16f, cy + s * 0.16f);
                        g.DrawLine(pen, cx + s * 0.16f, cy - s * 0.16f, cx - s * 0.16f, cy + s * 0.16f);
                        break;

                    case "copy":
                        using (var p = new Pen(color, Math.Max(1.2f, s / 11f)))
                        {
                            g.DrawRectangle(p, cx - s * 0.20f, cy - s * 0.12f, s * 0.26f, s * 0.30f);
                            g.DrawRectangle(p, cx - s * 0.06f, cy - s * 0.22f, s * 0.26f, s * 0.30f);
                        }
                        break;

                    case "paste":
                        using (var p = new Pen(color, Math.Max(1.2f, s / 11f)))
                        {
                            g.DrawRectangle(p, cx - s * 0.16f, cy - s * 0.16f, s * 0.32f, s * 0.34f);
                            g.DrawRectangle(p, cx - s * 0.07f, cy - s * 0.22f, s * 0.14f, s * 0.08f);
                        }
                        break;

                    case "zoom":
                        {
                            float r = s * 0.16f;
                            g.DrawEllipse(pen, cx - r - s * 0.06f, cy - r - s * 0.06f, r * 2, r * 2);
                            g.DrawLine(pen, cx + r * 0.6f, cy + r * 0.6f, cx + r + s * 0.12f, cy + r + s * 0.12f);
                        }
                        break;

                    default:
                        g.DrawEllipse(pen, cx - 3, cy - 3, 6, 6);
                        break;
                }
            }

            g.SmoothingMode = old;
        }

        private static void DrawStar(Graphics g, int cx, int cy, float r, Color color, bool filled)
        {
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double ang = -Math.PI / 2 + i * Math.PI / 5;
                double rad = (i % 2 == 0) ? r : r * 0.44;
                pts[i] = new PointF(cx + (float)(rad * Math.Cos(ang)), cy + (float)(rad * Math.Sin(ang)));
            }
            if (filled)
            {
                using (var b = new SolidBrush(color))
                    g.FillPolygon(b, pts);
            }
            else
            {
                using (var p = new Pen(color, Math.Max(1.3f, r / 7f)))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawPolygon(p, pts);
                }
            }
        }
    }
}
