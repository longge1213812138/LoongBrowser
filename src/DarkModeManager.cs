// DarkModeManager.cs —— 暗色模式管理：状态切换、设置持久化、网页 CSS 注入
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>暗色模式的可持久化设置：%APPDATA%\LoongBrowser\darkmode.json</summary>
    public class DarkModeSettings
    {
        public bool Enabled = false;
    }

    /// <summary>暗色模式管理器：负责切换状态、记住选择、提供网页注入 CSS</summary>
    public static class DarkModeManager
    {
        private static readonly string SettingsPath = Path.Combine(AppPaths.DataDir, "darkmode.json");
        private static DarkModeSettings _settings;

        /// <summary>暗色模式状态变化事件</summary>
        public static event Action<bool> DarkModeChanged;

        /// <summary>当前是否启用暗色模式</summary>
        public static bool IsEnabled
        {
            get { return Settings.Enabled; }
            set
            {
                if (Settings.Enabled == value) return;
                Settings.Enabled = value;
                JsonStore.Save(SettingsPath, Settings);
                if (DarkModeChanged != null)
                    DarkModeChanged(value);
            }
        }

        /// <summary>切换暗色模式</summary>
        public static void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        private static DarkModeSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = JsonStore.Load<DarkModeSettings>(SettingsPath);
                    if (_settings == null) _settings = new DarkModeSettings();
                }
                return _settings;
            }
        }

        // ========== 界面颜色定义 ==========

        /// <summary>暗色模式下的窗口背景色</summary>
        public static readonly Color WindowBackColor = Color.FromArgb(30, 30, 30);

        /// <summary>暗色模式下的前景文字色</summary>
        public static readonly Color WindowForeColor = Color.FromArgb(220, 220, 220);

        /// <summary>暗色模式下的控件背景色（地址栏、工具栏等）</summary>
        public static readonly Color ControlBackColor = Color.FromArgb(45, 45, 45);

        /// <summary>暗色模式下的控件前景色</summary>
        public static readonly Color ControlForeColor = Color.FromArgb(210, 210, 210);

        /// <summary>暗色模式下的菜单背景色</summary>
        public static readonly Color MenuBackColor = Color.FromArgb(37, 37, 37);

        /// <summary>暗色模式下的菜单前景色</summary>
        public static readonly Color MenuForeColor = Color.FromArgb(210, 210, 210);

        /// <summary>暗色模式下的工具栏背景色</summary>
        public static readonly Color ToolStripBackColor = Color.FromArgb(40, 40, 40);

        /// <summary>暗色模式下的标签条背景色</summary>
        public static readonly Color TabStripBackColor = Color.FromArgb(35, 35, 35);

        /// <summary>亮色模式下的窗口背景色</summary>
        public static readonly Color LightWindowBackColor = SystemColors.Control;

        /// <summary>亮色模式下的窗口前景色</summary>
        public static readonly Color LightWindowForeColor = SystemColors.ControlText;

        // ========== 网页暗色模式 CSS ==========

        /// <summary>注入到网页的 CSS，用于反转亮色背景为暗色</summary>
        public static string GetDarkModeCss()
        {
            return @"
/* LoongBrowser Dark Mode Injection */
:root {
    color-scheme: dark !important;
}
html, body {
    background-color: #1e1e1e !important;
    color: #e0e0e0 !important;
}
/* 常见亮色背景元素 */
body, div, section, article, main, aside, header, footer, nav,
p, span, a, li, td, th, tr, table, form, input, textarea, button,
select, option, pre, code, blockquote, figure, figcaption, h1, h2, h3, h4, h5, h6 {
    background-color: #2d2d2d !important;
    color: #e0e0e0 !important;
    border-color: #404040 !important;
}
/* 链接颜色 */
a, a:link, a:visited {
    color: #6cb4ee !important;
}
a:hover, a:active {
    color: #8ec8f6 !important;
}
/* 输入框和按钮 */
input, textarea, select, button {
    background-color: #3c3c3c !important;
    color: #e0e0e0 !important;
    border-color: #555 !important;
}
input:focus, textarea:focus, select:focus {
    border-color: #6cb4ee !important;
    outline-color: #6cb4ee !important;
}
/* 图片容器背景透明 */
img {
    background-color: transparent !important;
}
/* 滚动条样式 */
::-webkit-scrollbar {
    width: 10px;
    height: 10px;
}
::-webkit-scrollbar-track {
    background: #2d2d2d;
}
::-webkit-scrollbar-thumb {
    background: #555;
    border-radius: 5px;
}
::-webkit-scrollbar-thumb:hover {
    background: #777;
}
/* 代码块 */
pre, code, kbd, samp {
    background-color: #1a1a1a !important;
    color: #d4d4d4 !important;
}
/* 引用块 */
blockquote {
    border-left-color: #6cb4ee !important;
    background-color: #2a2a2a !important;
}
/* 表格条纹 */
tr:nth-child(even) {
    background-color: #2a2a2a !important;
}
tr:nth-child(odd) {
    background-color: #333 !important;
}
/* 禁止网站自己的浅色模式覆盖 */
@media (prefers-color-scheme: light) {
    :root {
        color-scheme: dark !important;
    }
}
";
        }

        /// <summary>亮色模式 CSS（清空暗色样式）</summary>
        public static string GetLightModeCss()
        {
            return @"
/* LoongBrowser Light Mode - Reset */
:root {
    color-scheme: light !important;
}
";
        }

        /// <summary>获取用于 ApplyCss 的完整 CSS 代码</summary>
        public static string GetInjectionCss()
        {
            return IsEnabled ? GetDarkModeCss() : GetLightModeCss();
        }

        /// <summary>获取用于 ExecuteScriptAsync 的完整 JavaScript 代码</summary>
        public static string GetInjectionScript()
        {
            string css = GetInjectionCss();
            // 转义 CSS 中的特殊字符
            css = css.Replace("\\", "\\\\");
            css = css.Replace("'", "\\'");
            css = css.Replace("\n", " ");
            
            string script = "(function() {" +
                "var id = '__loongbrowser_darkmode';" +
                "var existing = document.getElementById(id);" +
                "if (existing) existing.remove();" +
                "var style = document.createElement('style');" +
                "style.id = id;" +
                "style.textContent = '" + css + "';" +
                "(document.head || document.documentElement).appendChild(style);" +
                "})();";
            return script;
        }

        // ========== 应用界面样式 ==========

        /// <summary>将窗体及其所有子控件应用暗色/亮色模式</summary>
        public static void ApplyToForm(Form form)
        {
            if (form == null) return;
            bool dark = IsEnabled;

            form.BackColor = dark ? WindowBackColor : LightWindowBackColor;
            form.ForeColor = dark ? WindowForeColor : LightWindowForeColor;

            ApplyToControls(form, dark);
        }

        private static void ApplyToControls(Control parent, bool dark)
        {
            foreach (Control c in parent.Controls)
            {
                MenuStrip menu = c as MenuStrip;
                ToolStrip toolStrip = c as ToolStrip;
                TabControl tabControl = c as TabControl;

                if (menu != null)
                {
                    menu.BackColor = dark ? MenuBackColor : SystemColors.Control;
                    menu.ForeColor = dark ? MenuForeColor : SystemColors.ControlText;
                    menu.Renderer = dark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
                    ApplyToMenuItems(menu.Items, dark);
                }
                else if (toolStrip != null)
                {
                    toolStrip.BackColor = dark ? ToolStripBackColor : SystemColors.Control;
                    toolStrip.ForeColor = dark ? ControlForeColor : SystemColors.ControlText;
                    ApplyToToolStripItems(toolStrip.Items, dark);
                }
                else if (tabControl != null)
                {
                    // TabControl 的背景由 TabStrip 处理
                }
                else
                {
                    c.BackColor = dark ? ControlBackColor : SystemColors.Control;
                    c.ForeColor = dark ? ControlForeColor : SystemColors.ControlText;
                }

                if (c.HasChildren)
                    ApplyToControls(c, dark);
            }
        }

        private static void ApplyToMenuItems(ToolStripItemCollection items, bool dark)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = dark ? MenuBackColor : SystemColors.Control;
                item.ForeColor = dark ? MenuForeColor : SystemColors.ControlText;
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                {
                    menuItem.DropDown.BackColor = dark ? MenuBackColor : SystemColors.Control;
                    menuItem.DropDown.ForeColor = dark ? MenuForeColor : SystemColors.ControlText;
                    ApplyToMenuItems(menuItem.DropDownItems, dark);
                }
            }
        }

        private static void ApplyToToolStripItems(ToolStripItemCollection items, bool dark)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = dark ? ToolStripBackColor : SystemColors.Control;
                item.ForeColor = dark ? ControlForeColor : SystemColors.ControlText;
            }
        }

        /// <summary>刷新所有已打开窗口的暗色模式样式</summary>
        public static void RefreshAllWindows()
        {
            foreach (Form form in Application.OpenForms)
            {
                ApplyToForm(form);
            }
        }
    }

    /// <summary>暗色模式菜单渲染器</summary>
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(DarkModeManager.MenuBackColor))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
            Color bgColor = e.Item.Selected ? Color.FromArgb(60, 60, 60) : DarkModeManager.MenuBackColor;
            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? DarkModeManager.MenuForeColor : Color.Gray;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
            using (var pen = new Pen(Color.FromArgb(60, 60, 60)))
            {
                int y = rect.Height / 2;
                e.Graphics.DrawLine(pen, 0, y, rect.Width, y);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 不绘制边框，更简洁
        }
    }
}
