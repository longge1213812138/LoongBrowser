// GlassChrome.cs —— 自绘的浏览器外壳：菜单栏 + 工具栏 + 地址栏 + 标签头
//
// 为什么自绘：MenuStrip / ToolStrip 无法在 Acrylic 背景上正确绘制（透明 BackColor
// 会画成父窗口色），而玻璃外壳要求"不铺底色、只画前景元素"。
//
// 关键限制（决定了整个绘制策略）：
//   GDI 的 SolidBrush **忽略 alpha**，颜色一律按不透明画。所以玻璃区不能"画半透明"，
//   只能"整块不画"，让窗口背后的 Acrylic 透出来。高光与阴影用 1px 实线画，
//   不透明元素（地址栏胶囊、选中标签、悬停反馈）照常用实色填充。
//
// 标签头（TabHeader）与内容区的关系：TabControl 的标签条无法分离，因此
// TabControl 保留为 WebView2 容器但把标签头压到最小（见 ADR-0004），
// 标签的视觉与点击命中全部由这里接管。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>工具栏按钮描述</summary>
    public class ChromeToolButton
    {
        public string Icon;
        public string Tooltip;
        public Action Click;
        /// <summary>分组之间的分隔线</summary>
        public bool Separator;
        public bool Enabled = true;
    }

    /// <summary>自绘的浏览器外壳</summary>
    public class GlassChrome : Control
    {
        // ---------- 布局常量 ----------
        private const int ButtonSize = 28;
        private const int ButtonGap = 2;
        private const int AddressHeight = 24;

        // ---------- 子区域（在 Relayout 里按当前宽度重算） ----------
        private Rectangle _menuBarRect = Rectangle.Empty;
        private Rectangle _toolBarRect = Rectangle.Empty;
        private Rectangle _tabHeaderRect = Rectangle.Empty;
        private Rectangle _addressRect = Rectangle.Empty;

        // ---------- 菜单 ----------
        private readonly List<ChromeMenuItem> _topMenus = new List<ChromeMenuItem>();
        private int _hotMenu = -1;
        private int _openMenu = -1;
        private ChromeDropDown _dropDown;

        // ---------- 工具按钮 ----------
        private readonly List<ChromeToolButton> _buttons = new List<ChromeToolButton>();
        private readonly List<Rectangle> _buttonRects = new List<Rectangle>();
        private int _hotButton = -1;

        // ---------- 地址栏 ----------
        private readonly TextBox _addressBox;
        private bool _addressFocused;

        // ---------- 标签头 ----------
        private TabControl _tabs;
        private int _hotTab = -1;
        private int _hotClose = -1;

        /// <summary>地址栏回车</summary>
        public event Action<string> AddressSubmit;

        /// <summary>标签关闭请求（参数为标签索引）</summary>
        public event Action<int> TabCloseRequested;

        public GlassChrome()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Opaque, true);   // 自己负责整块绘制（玻璃区不画）
            Dock = DockStyle.Top;
            // 不设 BackColor = Transparent：WinForms 不支持透明背景色，
            // 玻璃区靠"不绘制背景"实现（见文件顶部注释）。
            MenuBarVisible = true;
            ToolBarVisible = true;
            TabHeaderVisible = true;

            _addressBox = new TextBox();
            _addressBox.BorderStyle = BorderStyle.None;
            _addressBox.Font = VistaTheme.UIFont;
            _addressBox.ForeColor = VistaTheme.Text;
            _addressBox.BackColor = VistaTheme.SurfaceWhite;
            _addressBox.Visible = false;
            _addressBox.KeyDown += OnAddressKeyDown;
            _addressBox.GotFocus += delegate { _addressFocused = true; Invalidate(); };
            _addressBox.LostFocus += delegate { _addressFocused = false; Invalidate(); };
            Controls.Add(_addressBox);
        }

        /// <summary>地址栏文本框（供外部读写、获取焦点）</summary>
        public TextBox AddressBox { get { return _addressBox; } }

        /// <summary>地址栏文字</summary>
        public string AddressText
        {
            get { return _addressBox.Text; }
            set { _addressBox.Text = value ?? ""; }
        }

        /// <summary>三段的可见性（全屏时由 FullscreenManager 统一控制）</summary>
        public bool MenuBarVisible { get; set; }
        public bool ToolBarVisible { get; set; }
        public bool TabHeaderVisible { get; set; }

        /// <summary>绑定标签头的数据源</summary>
        public void AttachTabs(TabControl tabs)
        {
            _tabs = tabs;
            if (_tabs != null)
            {
                _tabs.SelectedIndexChanged += delegate { Invalidate(); };
                _tabs.ControlAdded += delegate { Relayout(); Invalidate(); };
                _tabs.ControlRemoved += delegate { Relayout(); Invalidate(); };
            }
            Relayout();
        }

        // ---------- 对外配置 ----------

        /// <summary>设置菜单（由 MainForm 调用）</summary>
        public void SetMenus(List<ChromeMenuItem> menus)
        {
            _topMenus.Clear();
            if (menus != null) _topMenus.AddRange(menus);
            Relayout();
            Invalidate();
        }

        /// <summary>设置工具按钮</summary>
        public void SetButtons(List<ChromeToolButton> buttons)
        {
            _buttons.Clear();
            if (buttons != null) _buttons.AddRange(buttons);
            Invalidate();
        }

        /// <summary>可见性变化后重新布局（全屏切换调用）</summary>
        public void ApplyVisibility()
        {
            Relayout();
            Invalidate();
        }

        // ---------- 布局 ----------

        private void Relayout()
        {
            int y = 0;
            if (MenuBarVisible && _topMenus.Count > 0)
            {
                _menuBarRect = new Rectangle(0, y, Width, VistaTheme.MenuBarHeight);
                y += VistaTheme.MenuBarHeight;
            }
            else _menuBarRect = Rectangle.Empty;

            if (ToolBarVisible)
            {
                _toolBarRect = new Rectangle(0, y, Width, VistaTheme.ToolBarHeight);
                y += VistaTheme.ToolBarHeight;
            }
            else _toolBarRect = Rectangle.Empty;

            if (TabHeaderVisible && _tabs != null)
            {
                _tabHeaderRect = new Rectangle(0, y, Width, TabStrip.TabHeight);
                y += TabStrip.TabHeight;
            }
            else _tabHeaderRect = Rectangle.Empty;

            if (Height != y) Height = y;
            _addressBox.Visible = _toolBarRect.Height > 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        /// <summary>重算工具按钮位置，返回地址栏左侧起点</summary>
        private Rectangle LayoutButtons()
        {
            _buttonRects.Clear();
            if (_toolBarRect.Height == 0) return Rectangle.Empty;

            int x = 6;
            int cy = _toolBarRect.Y + (_toolBarRect.Height - ButtonSize) / 2;

            for (int i = 0; i < _buttons.Count; i++)
            {
                ChromeToolButton b = _buttons[i];
                if (b.Separator)
                {
                    _buttonRects.Add(new Rectangle(x + 4, cy + 6, 1, ButtonSize - 12));
                    x += 10;
                    continue;
                }
                _buttonRects.Add(new Rectangle(x, cy, ButtonSize, ButtonSize));
                x += ButtonSize + ButtonGap;
            }
            return new Rectangle(x + 4, cy + (ButtonSize - AddressHeight) / 2, 0, AddressHeight);
        }

        // ---------- 绘制 ----------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 留空：不铺底色。Acrylic 从这里透出来。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_menuBarRect.Height > 0) DrawMenuBar(g);
            if (_toolBarRect.Height > 0) DrawToolBar(g);
            if (_tabHeaderRect.Height > 0) DrawTabHeader(g);
        }

        private void DrawMenuBar(Graphics g)
        {
            VistaTheme.DrawEdgeHighlight(g, _menuBarRect);

            int x = 8;
            for (int i = 0; i < _topMenus.Count; i++)
            {
                ChromeMenuItem m = _topMenus[i];
                // 宽度必须与绘制同口径（GDI+）：DrawChromeText 用 DrawString，
                // 若这里仍用 TextRenderer.MeasureText（GDI），菜单项矩形会偏移几个像素。
                int textWidth = VistaTheme.MeasureWidth(g, m.Text, VistaTheme.UIFont);
                var r = new Rectangle(x, _menuBarRect.Y + 2, textWidth + 18, _menuBarRect.Height - 4);

                if (i == _openMenu)
                    VistaTheme.DrawSurface(g, r, VistaTheme.AccentBlueLight, VistaTheme.AccentBlue, 4);
                else if (i == _hotMenu)
                    VistaTheme.DrawSurface(g, r, Color.FromArgb(235, 244, 252), Color.Empty, 4);

                VistaTheme.DrawChromeText(g, m.Text, VistaTheme.UIFont, r, VistaTheme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                m.LayoutRect = r;
                x = r.Right + 2;
            }
        }

        private void DrawToolBar(Graphics g)
        {
            Rectangle addrStart = LayoutButtons();

            int bi = 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                Rectangle r = _buttonRects[bi++];
                ChromeToolButton b = _buttons[i];

                if (b.Separator)
                {
                    using (var pen = new Pen(Color.FromArgb(190, 205, 219), 1f))
                        g.DrawLine(pen, r.X, r.Y, r.X, r.Y + r.Height);
                    continue;
                }

                if (i == _hotButton && b.Enabled)
                    VistaTheme.DrawSurface(g, r, Color.FromArgb(235, 244, 252), VistaTheme.AccentBlue, 5);

                Color iconColor = b.Enabled ? VistaTheme.Text : VistaTheme.TextDisabled;
                var iconRect = new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10);
                ChromeIcons.Draw(g, b.Icon, iconRect, iconColor);
            }

            // 地址栏胶囊：不透明白底 + 冷蓝描边（Vista 的辨识元素）
            int addrRight = Width - 150;
            if (_buttons.Count > 0 && addrRight - addrStart.X > 120)
            {
                _addressRect = new Rectangle(addrStart.X, addrStart.Y, addrRight - addrStart.X, AddressHeight);
                VistaTheme.DrawSurface(g, _addressRect, VistaTheme.SurfaceWhite,
                    _addressFocused ? VistaTheme.AccentBlue : VistaTheme.SurfaceBorder, 5);

                int pad = 7;
                _addressBox.Bounds = new Rectangle(_addressRect.X + pad, _addressRect.Y + 3,
                    Math.Max(40, _addressRect.Width - pad * 2), _addressRect.Height - 6);
                _addressBox.Visible = true;
            }
            else
            {
                _addressRect = Rectangle.Empty;
                _addressBox.Visible = false;
            }

            VistaTheme.DrawEdgeHighlight(g, _toolBarRect);
        }

        private Rectangle TabRectAt(int index)
        {
            int x = 6;
            for (int i = 0; i < index; i++)
                x += TabStrip.TabWidth + 4;
            return new Rectangle(x, _tabHeaderRect.Y + 4, TabStrip.TabWidth, TabStrip.TabHeight - 6);
        }

        private void DrawTabHeader(Graphics g)
        {
            if (_tabs == null) return;

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                var r = TabRectAt(i);
                if (r.Right > Width - 6) break;

                bool selected = i == _tabs.SelectedIndex;

                if (selected)
                {
                    VistaTheme.DrawSurface(g, r, VistaTheme.SurfaceWhite, VistaTheme.AccentBlue, 6);
                    using (var pen = new Pen(VistaTheme.HighlightTop, 1f))
                        g.DrawLine(pen, r.X + 6, r.Y + 1, r.Right - 6, r.Y + 1);
                }
                else
                {
                    VistaTheme.DrawSurface(g, r, VistaTheme.SurfaceDim,
                        Color.FromArgb(206, 216, 227), 6);
                }

                var closeRect = CloseRectOf(r);
                if (i == _hotClose)
                {
                    using (var b = new SolidBrush(selected ? VistaTheme.CloseHover : Color.FromArgb(206, 216, 227)))
                        g.FillEllipse(b, closeRect);
                }
                // 页签两态都纯黑（用户 2026-10 指定），未选中不再用灰
                ChromeIcons.Draw(g, "close", closeRect,
                    (i == _hotClose && selected) ? Color.White : VistaTheme.Text);

                string title = _tabs.TabPages[i].Text;
                if (string.IsNullOrEmpty(title)) title = "新标签页";
                int textWidth = closeRect.Left - r.X - 16;
                var textRect = new Rectangle(r.X + 10, r.Y, Math.Max(0, textWidth), r.Height);
                // 两态都纯黑：选中态由卡片底色（白 / #EEF2F7）区分，不再靠灰字
                title = VistaTheme.Ellipsize(g, title, VistaTheme.UIFont, textRect.Width);
                VistaTheme.DrawChromeText(g, title, VistaTheme.UIFont, textRect, VistaTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            VistaTheme.DrawEdgeHighlight(g, _tabHeaderRect);
        }

        private Rectangle CloseRectOf(Rectangle tabRect)
        {
            return new Rectangle(tabRect.Right - 22, tabRect.Y + (tabRect.Height - 16) / 2, 16, 16);
        }

        // ---------- 命中测试 ----------

        private int MenuHitTest(Point p)
        {
            for (int i = 0; i < _topMenus.Count; i++)
                if (_topMenus[i].LayoutRect.Width > 0 && _topMenus[i].LayoutRect.Contains(p)) return i;
            return -1;
        }

        private int ButtonHitTest(Point p)
        {
            int bi = 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                Rectangle r = _buttonRects[bi++];
                if (_buttons[i].Separator) continue;
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        private int TabHitTest(Point p, out bool onClose)
        {
            onClose = false;
            if (_tabHeaderRect.Height == 0 || _tabs == null) return -1;

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                var r = TabRectAt(i);
                if (r.Right > Width - 6) break;
                if (r.Contains(p))
                {
                    onClose = CloseRectOf(r).Contains(p);
                    return i;
                }
                if (p.X < r.X) break;
            }
            return -1;
        }

        // ---------- 交互 ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int menu = MenuHitTest(e.Location);
            int button = ButtonHitTest(e.Location);
            bool onClose;
            int tab = TabHitTest(e.Location, out onClose);

            int newHotClose = (tab >= 0 && onClose) ? tab : -1;

            if (menu != _hotMenu || button != _hotButton || tab != _hotTab || newHotClose != _hotClose)
            {
                _hotMenu = menu;
                _hotButton = button;
                _hotTab = tab;
                _hotClose = newHotClose;
                Cursor = (button >= 0 || menu >= 0 || newHotClose >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotMenu = -1;
            _hotButton = -1;
            _hotTab = -1;
            _hotClose = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int menu = MenuHitTest(e.Location);
            if (menu >= 0) { OpenMenu(menu); return; }

            int button = ButtonHitTest(e.Location);
            if (button >= 0)
            {
                ChromeToolButton b = _buttons[button];
                if (b != null && b.Click != null && b.Enabled) b.Click();
                return;
            }

            bool onClose;
            int tab = TabHitTest(e.Location, out onClose);
            if (tab >= 0)
            {
                if (onClose)
                {
                    if (TabCloseRequested != null) TabCloseRequested(tab);
                }
                else if (_tabs != null)
                {
                    _tabs.SelectedIndex = tab;
                    Invalidate();
                }
            }
        }

        private void OpenMenu(int index)
        {
            if (_dropDown != null && _dropDown.Visible) _dropDown.Close();
            _openMenu = index;
            Invalidate();

            ChromeMenuItem m = _topMenus[index];
            if (!m.HasChildren) return;

            _dropDown = new ChromeDropDown(m.Children, 170);
            Point screen = PointToScreen(new Point(m.LayoutRect.X, m.LayoutRect.Bottom));
            _dropDown.ShowAt(screen);
            _dropDown.FormClosed += delegate
            {
                _openMenu = -1;
                Invalidate();
            };
        }

        private void OnAddressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                string t = _addressBox.Text;
                if (AddressSubmit != null) AddressSubmit(t);
            }
        }

        /// <summary>把焦点交给地址栏并全选（Ctrl+L 用）</summary>
        public void FocusAddress()
        {
            _addressBox.Focus();
            _addressBox.SelectAll();
        }
    }
}
