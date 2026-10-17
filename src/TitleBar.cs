// TitleBar.cs —— 自绘标题栏（Vista 风格）
//
// FormBorderStyle.None 之后，窗口失去系统标题栏。本控件补回：
//   · 拖动移动、双击最大化/还原、最大化时拖拽还原（与系统行为一致）
//   · 最小化 / 最大化 / 关闭三个自绘按钮
//   · 标题文字（左对齐，带图标位）
//
// 与全屏的关系：FullscreenManager 进入真全屏时把本控件隐藏，退出时恢复。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>Vista 风格的自绘标题栏</summary>
    public class TitleBar : Control
    {
        private enum HotPart { None, Minimize, Maximize, Close }

        private HotPart _hot = HotPart.None;
        private Form _form;
        private string _titleText = "LoongBrowser";

        /// <summary>标题文字</summary>
        public string TitleText
        {
            get { return _titleText; }
            set
            {
                _titleText = value ?? "";
                Invalidate();
            }
        }

        public TitleBar(Form owner)
        {
            _form = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            // 不绘制背景：Acrylic 从这里透出来
            SetStyle(ControlStyles.Opaque, true);
            Height = VistaTheme.TitleBarHeight;
            Dock = DockStyle.Top;
            // 不设 BackColor = Transparent：WinForms 不支持透明背景色，
            // 玻璃区靠"不绘制背景"实现（见 GlassChrome 顶部注释）。
        }

        // ---------- 布局 ----------

        private Rectangle CloseRect()
        {
            return new Rectangle(Width - VistaTheme.CaptionButtonWidth, 0,
                VistaTheme.CaptionButtonWidth, Height);
        }

        private Rectangle MaxRect()
        {
            return new Rectangle(Width - VistaTheme.CaptionButtonWidth * 2, 0,
                VistaTheme.CaptionButtonWidth, Height);
        }

        private Rectangle MinRect()
        {
            return new Rectangle(Width - VistaTheme.CaptionButtonWidth * 3, 0,
                VistaTheme.CaptionButtonWidth, Height);
        }

        private HotPart HitTest(Point p)
        {
            if (CloseRect().Contains(p)) return HotPart.Close;
            if (MaxRect().Contains(p)) return HotPart.Maximize;
            if (MinRect().Contains(p)) return HotPart.Minimize;
            return HotPart.None;
        }

        // ---------- 绘制 ----------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 留空：不铺底色，让 Acrylic 透出
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var full = new Rectangle(0, 0, Width, Height);

            // 高光只在顶部一条，底部阴影勾勒标题栏与工具栏的分界
            VistaTheme.DrawEdgeHighlight(g, full);

            // 标题文字
            int textLeft = 10;
            var textRect = new Rectangle(textLeft, 0,
                Math.Max(0, MinRect().Left - textLeft - 8), Height);
            TextRenderer.DrawText(g, VistaTheme.Ellipsize(g, _titleText, VistaTheme.UIFont, textRect.Width),
                VistaTheme.UIFont, textRect, VistaTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // 三个标题栏按钮
            DrawCaptionButton(g, MinRect(), HotPart.Minimize);
            DrawCaptionButton(g, MaxRect(), HotPart.Maximize);
            DrawCaptionButton(g, CloseRect(), HotPart.Close);
        }

        private void DrawCaptionButton(Graphics g, Rectangle r, HotPart part)
        {
            bool hot = _hot == part;

            if (hot)
            {
                Color fill = part == HotPart.Close ? VistaTheme.CloseHover : VistaTheme.CaptionHover;
                // 按钮是悬停反馈，允许不透明底 —— 它只占很小一块
                VistaTheme.DrawSurface(g, r, fill,
                    part == HotPart.Close ? Color.FromArgb(180, 60, 50) : VistaTheme.AccentBlue, 0);
            }

            Color glyphColor = (hot && part == HotPart.Close) ? Color.White : VistaTheme.CaptionGlyph;

            using (var pen = new Pen(glyphColor, 1.4f))
            using (var brush = new SolidBrush(glyphColor))
            {
                int cx = r.X + r.Width / 2;
                int cy = r.Y + r.Height / 2;

                if (part == HotPart.Minimize)
                {
                    g.DrawLine(pen, cx - 5, cy + 4, cx + 5, cy + 4);
                }
                else if (part == HotPart.Maximize)
                {
                    var box = new Rectangle(cx - 5, cy - 5, 10, 10);
                    if (_form != null && _form.WindowState == FormWindowState.Maximized)
                    {
                        // 还原图标：两个叠放的方框
                        g.DrawRectangle(pen, new Rectangle(cx - 5, cy - 3, 8, 8));
                        g.DrawLine(pen, cx - 3, cy - 5, cx + 5, cy - 5);
                        g.DrawLine(pen, cx + 5, cy - 5, cx + 5, cy + 3);
                    }
                    else
                    {
                        g.DrawRectangle(pen, box);
                    }
                }
                else if (part == HotPart.Close)
                {
                    g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                }
            }
        }

        // ---------- 交互 ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HotPart h = HitTest(e.Location);
            if (h != _hot)
            {
                _hot = h;
                Invalidate();
                Cursor = h == HotPart.None ? Cursors.Default : Cursors.Hand;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != HotPart.None)
            {
                _hot = HotPart.None;
                Invalidate();
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _form == null) return;

            HotPart h = HitTest(e.Location);
            if (h == HotPart.Close) { _form.Close(); return; }
            if (h == HotPart.Minimize) { _form.WindowState = FormWindowState.Minimized; return; }
            if (h == HotPart.Maximize)
            {
                _form.WindowState = _form.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                Invalidate();
                return;
            }

            // 空白处拖动窗口
            DragWindow();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || _form == null) return;
            if (HitTest(e.Location) != HotPart.None) return;   // 按钮上双击不算
            _form.WindowState = _form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            Invalidate();
        }

        /// <summary>用系统消息实现窗口拖动（自带 Snap 行为）</summary>
        private void DragWindow()
        {
            if (_form == null) return;
            ReleaseCapture();
            SendMessage(_form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
