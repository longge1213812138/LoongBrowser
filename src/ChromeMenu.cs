// ChromeMenu.cs —— 自绘菜单的模型与下拉面板
//
// MenuStrip 的透明支持很差（透明 BackColor 会画成父窗口色），玻璃外壳里用不了。
// 这里用最小的模型描述菜单项，下拉面板自绘。下拉本身是独立弹出窗口，
// 不参与玻璃（独立窗口无法共享母窗口的 Acrylic 合成层）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>一个菜单项（叶子或分组）</summary>
    public class ChromeMenuItem
    {
        public string Text;
        /// <summary>快捷键展示文字，仅用于显示（实际拦截在别处）</summary>
        public string Shortcut;
        public bool Checked;
        public bool Enabled = true;
        public bool IsSeparator;
        public Action Click;
        public List<ChromeMenuItem> Children;

        /// <summary>顶层菜单在菜单栏中的命中矩形（由 GlassChrome 布局时回填）</summary>
        public Rectangle LayoutRect;

        /// <summary>调用方自定义的附加值（如缩放档位的比例值）</summary>
        public object Tag;

        public bool HasChildren
        {
            get { return Children != null && Children.Count > 0; }
        }

        public static ChromeMenuItem Item(string text, Action click)
        {
            var m = new ChromeMenuItem();
            m.Text = text;
            m.Click = click;
            return m;
        }

        public static ChromeMenuItem Check(string text, bool isChecked, Action click)
        {
            var m = Item(text, click);
            m.Checked = isChecked;
            return m;
        }

        public static ChromeMenuItem Sub(string text, params ChromeMenuItem[] children)
        {
            var m = new ChromeMenuItem();
            m.Text = text;
            m.Children = new List<ChromeMenuItem>(children);
            return m;
        }

        public static ChromeMenuItem Separator()
        {
            var m = new ChromeMenuItem();
            m.IsSeparator = true;
            return m;
        }
    }

    /// <summary>自绘下拉面板（支持一级子菜单、勾选、快捷键展示）</summary>
    public class ChromeDropDown : Form
    {
        private const int ItemHeight = 26;
        private const int SeparatorHeight = 7;
        private const int PadX = 26;         // 左侧留白（给勾选标记）
        private const int PadRight = 26;
        private const int TextPad = 10;

        private readonly List<ChromeMenuItem> _items;
        private readonly int[] _heights;
        private int _hot = -1;
        private readonly int _menuWidth;
        private ChromeDropDown _child;

        private static readonly Color Back = Color.FromArgb(252, 252, 253);
        private static readonly Color Border = Color.FromArgb(150, 165, 182);
        private static readonly Color HotBack = Color.FromArgb(206, 228, 248);
        private static readonly Color TextColor = Color.FromArgb(28, 37, 48);
        private static readonly Color DisabledColor = Color.FromArgb(160, 170, 182);

        public ChromeDropDown(List<ChromeMenuItem> items, int minWidth)
        {
            _items = items;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);

            // 计算高度与宽度
            _heights = new int[_items.Count];
            int totalH = 4;
            for (int i = 0; i < _items.Count; i++)
            {
                _heights[i] = _items[i].IsSeparator ? SeparatorHeight : ItemHeight;
                totalH += _heights[i];
            }
            totalH += 4;

            int w = minWidth;
            using (Graphics g = CreateGraphics())
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    ChromeMenuItem m = _items[i];
                    if (m.IsSeparator) continue;
                    int need = PadX + TextPad
                             + TextRenderer.MeasureText(g, m.Text, VistaTheme.UIFont,
                                 new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                    if (!string.IsNullOrEmpty(m.Shortcut))
                        need += 30 + TextRenderer.MeasureText(g, m.Shortcut, VistaTheme.UIFont,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                    if (m.HasChildren) need += 22;
                    need += PadRight;
                    if (need > w) w = need;
                }
            }
            _menuWidth = w;
            ClientSize = new Size(w, totalH);
        }

        /// <summary>在指定屏幕位置弹出</summary>
        public void ShowAt(Point screenPos)
        {
            Rectangle screen = Screen.FromPoint(screenPos).WorkingArea;
            int x = Math.Min(screenPos.X, screen.Right - Width);
            int y = Math.Min(screenPos.Y, screen.Bottom - Height);
            Location = new Point(Math.Max(screen.Left, x), Math.Max(screen.Top, y));
            Show();
            Activate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_child != null && _child.Visible) return;
            Close();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private int YOf(int index)
        {
            int y = 4;
            for (int i = 0; i < index; i++) y += _heights[i];
            return y;
        }

        private int IndexAt(int y)
        {
            int acc = 4;
            for (int i = 0; i < _items.Count; i++)
            {
                if (y >= acc && y < acc + _heights[i]) return i;
                acc += _heights[i];
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var full = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = VistaTheme.Rounded(full, 4))
            using (var b = new SolidBrush(Back))
            using (var p = new Pen(Border, 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            for (int i = 0; i < _items.Count; i++)
            {
                ChromeMenuItem m = _items[i];
                int y = YOf(i);

                if (m.IsSeparator)
                {
                    using (var pen = new Pen(Color.FromArgb(214, 222, 231), 1f))
                        g.DrawLine(pen, 10, y + 3, Width - 10, y + 3);
                    continue;
                }

                var rowRect = new Rectangle(2, y, Width - 4, _heights[i]);
                if (i == _hot && m.Enabled)
                {
                    using (var path = VistaTheme.Rounded(rowRect, 3))
                    using (var b = new SolidBrush(HotBack))
                        g.FillPath(b, path);
                }

                Color fg = m.Enabled ? TextColor : DisabledColor;

                // 勾选标记
                if (m.Checked)
                {
                    using (var pen = new Pen(fg, 1.6f))
                    {
                        int cx = 16, cy = y + ItemHeight / 2;
                        g.DrawLine(pen, cx - 4, cy, cx - 1, cy + 3);
                        g.DrawLine(pen, cx - 1, cy + 3, cx + 4, cy - 4);
                    }
                }

                var textRect = new Rectangle(PadX + TextPad, y, Width - PadX - PadRight - TextPad, _heights[i]);
                TextRenderer.DrawText(g, m.Text, VistaTheme.UIFont, textRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                // 快捷键
                if (!string.IsNullOrEmpty(m.Shortcut))
                {
                    var sr = new Rectangle(Width - PadRight - 150, y, 150 - TextPad, _heights[i]);
                    TextRenderer.DrawText(g, m.Shortcut, VistaTheme.UIFont, sr, DisabledColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                // 子菜单箭头
                if (m.HasChildren)
                {
                    using (var pen = new Pen(fg, 1.4f))
                    {
                        int ax = Width - PadRight + 4;
                        int ay = y + ItemHeight / 2;
                        g.DrawLine(pen, ax, ay - 4, ax + 4, ay);
                        g.DrawLine(pen, ax + 4, ay, ax, ay + 4);
                    }
                }
            }
        }

        private void OpenChild(ChromeMenuItem parent, int index)
        {
            if (_child != null && _child.Visible) _child.Close();
            var child = new ChromeDropDown(parent.Children, 140);
            _child = child;
            int y = YOf(index);
            child.ShowAt(new Point(Left + Width - 4, Top + y));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = IndexAt(e.Y);
            if (idx != _hot)
            {
                _hot = idx;
                Invalidate();

                if (idx >= 0)
                {
                    ChromeMenuItem m = _items[idx];
                    if (m.HasChildren && m.Enabled) OpenChild(m, idx);
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1)
            {
                _hot = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = IndexAt(e.Y);
            if (idx < 0) return;
            ChromeMenuItem m = _items[idx];
            if (m.IsSeparator || !m.Enabled) return;
            if (m.HasChildren) { OpenChild(m, idx); return; }

            Close();
            if (m.Click != null) m.Click();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }
    }
}
