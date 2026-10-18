// WindowResizer.cs —— 无边框窗口的边缘 / 角落拖拽缩放
//
// 为什么不能只"返回 HTLEFT"就完事（实测，2026-10）：
//   · 不补 WS_THICKFRAME 时，系统根本不认为这个窗口可缩放，HT* 返回了也不会响应；
//   · 补了 WS_THICKFRAME 后，窗口会多出系统绘制的边框并把客户区往里挤，
//     因此必须同时回掉 WM_NCCALCSIZE，让客户区仍等于整个窗口矩形；
//   · 缩放循环是由**窗口**的 DefWindowProc 发起的。子控件（Panel/Control）即使
//     返回 HT* 也不会触发缩放，返回 HTTRANSPARENT 也一样不会 —— 实测六向全失败。
//     所以可拖拽的那一圈必须属于 Form 自己：MainForm 留出 Padding，
//     内容控件（标题栏 / 外壳 / 标签容器）缩在 Padding 以内，边缘带就是玻璃本身。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>无边框窗口的边缘 / 角落缩放</summary>
    public static class WindowResizer
    {
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;

        // 命中测试返回值（公开：测试要按名字核对，不写魔术数字）
        public const int HTCLIENT = 1;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        /// <summary>窗口风格：可缩放边框（系统据此才会执行缩放）</summary>
        public const int WS_THICKFRAME = 0x00040000;

        /// <summary>窗口风格：最大化按钮（无边框窗口仍需显式保留才有关闭/最大化语义）</summary>
        public const int WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>窗口风格：最小化按钮</summary>
        public const int WS_MINIMIZEBOX = 0x00020000;

        /// <summary>
        /// 命中测试：把一个客户区坐标判成 HT* 缩放码。
        /// 不在边缘带上时返回 HTCLIENT，调用方应继续走默认处理。
        /// </summary>
        public static int HitTest(Point client, Size size)
        {
            int band = VistaTheme.ResizeBorderWidth;
            if (band <= 0) return HTCLIENT;

            // 窗口太小时不要把整块都判成边缘，否则内部控件彻底点不到
            if (size.Width < band * 4 || size.Height < band * 4) return HTCLIENT;

            bool left = client.X < band;
            bool right = client.X >= size.Width - band;
            bool top = client.Y < band;
            bool bottom = client.Y >= size.Height - band;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return HTCLIENT;
        }

        /// <summary>从 WM_NCHITTEST 的 lParam 取出屏幕坐标，换算成客户区坐标</summary>
        public static int HitTestFromScreen(Control owner, long lParam)
        {
            if (owner == null) return HTCLIENT;
            int sx = unchecked((short)(lParam & 0xFFFF));
            int sy = unchecked((short)((lParam >> 16) & 0xFFFF));
            return HitTest(owner.PointToClient(new Point(sx, sy)), owner.ClientSize);
        }

        /// <summary>
        /// 处理无边框窗口的两个消息；返回 true 表示已处理，调用方不要再交给基类。
        /// </summary>
        public static bool HandleMessage(ref Message m, Form form)
        {
            if (form == null) return false;

            if (m.Msg == WM_NCCALCSIZE)
            {
                // 客户区 = 整个窗口矩形。不做这一步，WS_THICKFRAME 会把内容挤进边框里，
                // 玻璃外壳右边和下边会露出一条系统灰边。
                // 只处理 wParam != 0（有效矩形）这一种；最大化时也必须放行，
                // 否则客户区会比屏幕小一圈。窗口最大化时的溢出由 MaxmizedWindowBounds 修正。
                if (m.WParam != IntPtr.Zero)
                {
                    m.Result = IntPtr.Zero;
                    return true;
                }
                return false;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                // 最大化时不需要缩放（系统本来也不允许拖边改大小）
                if (form.WindowState == FormWindowState.Maximized) return false;

                int ht = HitTestFromScreen(form, m.LParam.ToInt64());
                if (ht != HTCLIENT)
                {
                    m.Result = (IntPtr)ht;
                    return true;
                }
            }
            return false;
        }

        private const int WM_GETMINMAXINFO = 0x0024;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        /// <summary>
        /// 拖拽缩放时允许的最小窗口尺寸。
        /// </summary>
        /// <remarks>
        /// 直接取 Form.MinimumSize：无边框窗口的客户区等于整个窗口矩形（NCCALCSIZE 已归零），
        /// 所以两者是同一套坐标，不需要再补边框宽。
        /// 为什么非得自己写进 ptMinTrackSize：WinForms 是在 Form.WndProc 之后才处理
        /// WM_GETMINMAXINFO 的，那一刻 lParam 里还是系统默认值（实测 136x39）；
        /// 不写进去，拖拽缩放就能绕过 MinimumSize 把窗口拖到 200px 宽。
        /// </remarks>
        public static Size MinimumTrackSize(Size formMinimum)
        {
            return new Size(Math.Max(0, formMinimum.Width), Math.Max(0, formMinimum.Height));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 修正最大化时的窗口矩形，避免无边框 + WS_THICKFRAME 导致窗口比工作区大一圈。
        /// </summary>
        /// <remarks>
        /// 无边框窗口最大化时，系统按"带边框窗口"的规则把窗口撑到 屏幕+边框，
        /// 于是任务栏会被压住一小条、左右各溢出几像素。这里把最大化矩形的
        /// 位置与尺寸钉到监视器工作区上，视觉与行为都回到正常最大化。
        /// </remarks>
        public static bool HandleMinMaxInfo(ref Message m, Form form)
        {
            if (form == null || m.Msg != WM_GETMINMAXINFO) return false;
            try
            {
                IntPtr monitor = MonitorFromWindow(form.Handle, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info)) return false;

                var mmi = (MINMAXINFO)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    m.LParam, typeof(MINMAXINFO));

                // 尺寸：工作区大小（不含任务栏）；位置：工作区左上角
                mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;

                // 最大可跟踪尺寸钉到工作区，否则拖到屏幕边缘时会溢出
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;

                // 最小跟踪尺寸必须显式写入：WinForms 是在 Form.WndProc 之后才处理这条消息的，
                // 此刻 lParam 里还是系统默认值（实测 136,39）。不写进去，拖拽缩放就能
                // 绕过 MinimumSize 把窗口拖到 200px 宽 —— 外壳会挤成一团。
                Size minimum = MinimumTrackSize(form.MinimumSize);
                if (minimum.Width > 0) mmi.ptMinTrackSize.X = minimum.Width;
                if (minimum.Height > 0) mmi.ptMinTrackSize.Y = minimum.Height;

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, m.LParam, false);
                m.Result = IntPtr.Zero;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
