// WindowResizer.cs —— 无边框窗口的系统级缩放 / 拖动 / 分屏（不靠 WS_THICKFRAME）
//
// 背景（重要，别改）：
//   · 用户要求"玻璃常驻、不做降级"。实测（2026-10，见 docs/adr/0010）：
//     只要窗口样式带 WS_THICKFRAME，DWM 就会在该窗口**失焦时停用 Acrylic**，
//     前台 sat=23 → 失焦 sat=0，且重新 Apply 也救不回来。
//   · 但"系统缩放"并不等于"必须有 WS_THICKFRAME"。
//     Windows 只需要窗口对 WM_NCHITTEST 回答 HTLEFT/HTRIGHT/... 就会启动系统的
//     尺寸循环（拖边缩放、双击标题栏最大化、拖到屏幕边缘 Aero Snap 分屏全都回来）。
//
// 所以做法是：
//   1. 窗口仍是 FormBorderStyle.None、样式里**没有** WS_THICKFRAME（玻璃安全）；
//   2. 拦截 WM_NCHITTEST：把四周 6px 判成 HTLEFT 等，把顶部标题栏判成 HTCAPTION；
//   3. 系统收到这些返回值，自己起缩放/拖动循环 —— 分屏、贴边、双击最大化全由系统负责，
//      行为与资源管理器、记事本等原生窗口完全一致（这是用户要的"按 Windows 规范"）。
//
// 与"自己改 Bounds"的旧做法相比，这里交给系统的好处正是用户报的那几点：
//   · 拖到屏幕边缘会出 Aero Snap 预览并半屏吸附；
//   · 拖到顶部会最大化；拖离后还原到原来的大小与位置；
//   · 多显示器、DPI、任务栏的边界处理都由系统算，不用我们重复实现。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>无边框窗口的系统级缩放与分屏（WM_NCHITTEST 路线）</summary>
    public static class WindowResizer
    {
        /// <summary>窗口风格：最小化按钮</summary>
        public const int WS_MINIMIZEBOX = 0x00020000;

        /// <summary>窗口风格：最大化按钮</summary>
        public const int WS_MAXIMIZEBOX = 0x00010000;

        // ---------- WM_NCHITTEST 的返回值（WinUser.h） ----------
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

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

        /// <summary>边缘 → WM_NCHITTEST 返回值；Edge.None 返回 HTCLIENT</summary>
        public static int HitTestCode(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top: return HTTOP;
                case Edge.Bottom: return HTBOTTOM;
                case Edge.Left: return HTLEFT;
                case Edge.Right: return HTRIGHT;
                case Edge.Top | Edge.Left: return HTTOPLEFT;
                case Edge.Top | Edge.Right: return HTTOPRIGHT;
                case Edge.Bottom | Edge.Left: return HTBOTTOMLEFT;
                case Edge.Bottom | Edge.Right: return HTBOTTOMRIGHT;
                default: return HTCLIENT;
            }
        }

        /// <summary>边缘对应的鼠标指针（非最大化时由系统按 HT 值给出，这里供自绘/测试使用）</summary>
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
    }
}
