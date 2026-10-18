// ChromeTooltip.cs —— 悬浮提示窗：画在外壳之上，不占外壳布局
//
// 为什么不用系统 ToolTip：它用系统主题的不透明底，和玻璃外壳的观感不一致。
// 为什么不能画在 GlassChrome 内部：提示要挂在工具栏下方，那里是标签条，
//   叠上去会盖住标签文字（实测截图可见）。
// 为什么要分层窗口：窗口默认不透明，矩形白块会把下面的标签条糊掉；
//   用 WS_EX_LAYERED + 色键，只有提示框本身的像素出现，其余完全透明。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>悬停提示窗（无边框、不进任务栏、不抢焦点、背景色键透明）</summary>
    public class ChromeTooltip : Form
    {
        // 色键：这个颜色在分层窗口里被完全透明化。故意选一个不会被画到的颜色。
        private static readonly Color Transparent = Color.FromArgb(255, 0, 255);

        private string _text = "";

        public ChromeTooltip()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Transparent;
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
        }

        /// <summary>不抢焦点：提示出现时用户仍能继续点按钮</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不进 Alt+Tab
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE：不抢焦点
                // 鼠标穿透：提示窗永远不接收鼠标消息，等价于"画上去的一张纸"。
                // 不加这条，提示一旦与光标重叠（提示就在按钮右侧，光标常靠近按钮右沿），
                // 系统把鼠标改判给提示窗、宿主收到 WM_MOUSELEAVE，
                // 提示被自己赶走 —— 用户看到的就是"一闪一闪"。
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 色键透明：只有提示框的像素可见，窗口其余部分透出底下的内容
            Native.SetLayeredWindowAttributes(Handle, (uint)Transparent.ToArgb(), 0, 0x00000001);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            VistaTheme.DrawSurface(g, r, VistaTheme.SurfaceWhite, VistaTheme.SurfaceBorder, 4);
            VistaTheme.DrawChromeText(g, _text, VistaTheme.UIFont,
                new Rectangle(8, 2, Math.Max(0, Width - 16), Math.Max(0, Height - 4)),
                VistaTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>按文字算出窗口尺寸</summary>
        public static Size Measure(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                int w = VistaTheme.MeasureWidth(g, text, font) + 18;
                // 高度按"单行文字 + 4px 内边距"收紧：玻璃外壳上方只有 26px（标题栏），
                // 提示做得太高就塞不进按钮上方，只能翻到下方压住标签条。
                int h = (int)Math.Ceiling(g.MeasureString(text, font).Height) + 4;
                return new Size(Math.Max(24, w), Math.Max(18, h));
            }
        }

        /// <summary>在屏幕坐标处显示</summary>
        public void ShowText(string text, Point screenLocation)
        {
            _text = text ?? "";
            Size size = Measure(_text, VistaTheme.UIFont);
            if (size == Size.Empty) return;

            Bounds = new Rectangle(screenLocation, size);
            if (!Visible)
            {
                Show();
                // Show 之后句柄才真正带上一整套扩展样式；色键必须在句柄建立后再设一次，
                // 否则首次显示时窗口还是一块不透明的洋红底（实测：首次弹不出/闪）。
                if (IsHandleCreated)
                    Native.SetLayeredWindowAttributes(Handle, (uint)Transparent.ToArgb(), 0, 0x00000001);
            }
            Invalidate();
            Update();
        }

        private static class Native
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
        }
    }
}
