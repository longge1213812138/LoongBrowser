// WindowResizer.cs —— 无边框窗口的边缘 / 角落拖拽缩放（自实现，不用 WS_THICKFRAME）
//
// 为什么不用系统缩放（实测，2026-10，重要）：
//   · 系统缩放必须补 WS_THICKFRAME，但**补了它，DWM 在窗口失活时会停用 Acrylic**：
//     失焦后整个窗口变成不透明纯色块，玻璃彻底消失，重新 Apply / 重绘 / 重设边框
//     都无法恢复（二分实验：同窗口只加这一位，失焦即丢玻璃；去掉它，玻璃常驻）。
//   · 这条约束是硬性的：用户要的是"玻璃常驻、不做降级"，那就不能要 WS_THICKFRAME。
//
// 所以缩放由这里自己实现：边缘一圈热区（MainForm.Padding 让出来）按下后进入拖拽态，
// 按鼠标位移直接改 Bounds。这圈热区同时就是玻璃本身 —— Form 只清面不铺色。
//
// 与系统缩放的行为差异（有意为之）：
//   · 拖到屏幕边缘不会自动 Snap 半屏：那需要系统缩放循环，与玻璃常驻冲突，不做。
//   · 最小尺寸由 MinimumSize 在拖拽时钳制。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>无边框窗口的边缘 / 角落拖拽缩放（自实现）</summary>
    public static class WindowResizer
    {
        /// <summary>窗口风格：最小化按钮</summary>
        public const int WS_MINIMIZEBOX = 0x00020000;

        /// <summary>窗口风格：最大化按钮</summary>
        public const int WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>命中区域</summary>
        public enum Edge
        {
            None = 0,
            Top = 1,
            Bottom = 2,
            Left = 4,
            Right = 8
        }

        /// <summary>把客户区坐标判成边缘组合；不在边带上时返回 Edge.None</summary>
        public static Edge HitTest(Point client, Size size)
        {
            int band = VistaTheme.ResizeBorderWidth;
            if (band <= 0) return Edge.None;

            // 窗口太小时不要把整块都判成边缘，否则内部控件彻底点不到
            if (size.Width < band * 4 || size.Height < band * 4) return Edge.None;

            Edge e = Edge.None;
            if (client.Y < band) e |= Edge.Top;
            if (client.Y >= size.Height - band) e |= Edge.Bottom;
            if (client.X < band) e |= Edge.Left;
            if (client.X >= size.Width - band) e |= Edge.Right;
            return e;
        }

        /// <summary>边缘对应的鼠标指针</summary>
        public static Cursor CursorFor(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top | Edge.Left:
                case Edge.Bottom | Edge.Right:
                    return Cursors.SizeNWSE;
                case Edge.Top | Edge.Right:
                case Edge.Bottom | Edge.Left:
                    return Cursors.SizeNESW;
                case Edge.Left:
                case Edge.Right:
                    return Cursors.SizeWE;
                case Edge.Top:
                case Edge.Bottom:
                    return Cursors.SizeNS;
                default:
                    return Cursors.Default;
            }
        }

        /// <summary>
        /// 一次拖拽会话：按下时记录起点与原始矩形，移动时按位移推出新矩形。
        /// </summary>
        /// <remarks>
        /// 用类而不是几个静态字段：多窗口（进程内多开）各自持有一份，互不串状态。
        /// </remarks>
        public sealed class DragSession
        {
            private readonly Form _form;
            private readonly Edge _edge;
            private readonly Point _origin;
            private readonly Rectangle _startBounds;
            private readonly Size _minimum;

            public DragSession(Form form, Edge edge, Rectangle startBounds, Point screenOrigin, Size minimum)
            {
                _form = form;
                _edge = edge;
                _startBounds = startBounds;
                _origin = screenOrigin;
                _minimum = minimum;
            }

            /// <summary>按当前鼠标屏幕坐标更新窗口矩形</summary>
            public void Update(Point screenNow)
            {
                if (_form == null || _form.IsDisposed) return;

                int dx = screenNow.X - _origin.X;
                int dy = screenNow.Y - _origin.Y;

                Rectangle b = _startBounds;
                if ((_edge & Edge.Top) != 0) { b.Y += dy; b.Height -= dy; }
                if ((_edge & Edge.Bottom) != 0) b.Height += dy;
                if ((_edge & Edge.Left) != 0) { b.X += dx; b.Width -= dx; }
                if ((_edge & Edge.Right) != 0) b.Width += dx;

                // 钳制最小尺寸：拖到下限后继续拖，边不再跟着走（与系统行为一致）
                int minW = Math.Max(1, _minimum.Width);
                int minH = Math.Max(1, _minimum.Height);
                if (b.Width < minW)
                {
                    if ((_edge & Edge.Left) != 0) b.X = _startBounds.Right - minW;
                    b.Width = minW;
                }
                if (b.Height < minH)
                {
                    if ((_edge & Edge.Top) != 0) b.Y = _startBounds.Bottom - minH;
                    b.Height = minH;
                }

                if (_form.Bounds != b) _form.Bounds = b;
            }
        }
    }
}
