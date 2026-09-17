// TabStripRenderTest.cs —— 标签页外观的单元测试 + 渲染截图
//
// 除断言样式参数外，把标签条画成 PNG（tests\ui_tabs_preview.png），
// 这样"字号变大、标签之间有间隔、尺寸统一"是看得见的，而不是只靠代码推断。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class TabStripRenderTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string log = Path.Combine(baseDir, "tabstrip_result.txt");
            string shot = Path.Combine(baseDir, "ui_tabs_preview.png");

            _sb.AppendLine("=== 标签页外观单元测试 ===");
            _sb.AppendLine();

            var form = new Form();
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.Size = new Size(1000, 300);

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            TabStrip.Style(tabs);

            string[] titles = new string[]
            {
                "百度一下，你就知道",
                "Example Domain",
                "特别特别长的标签页标题用来验证超出宽度时的省略号处理",
                ""                                        // 空标题 → 回退成"新标签页"
            };
            for (int i = 0; i < titles.Length; i++)
            {
                var p = new TabPage(titles[i]);
                p.BackColor = Color.White;
                tabs.TabPages.Add(p);
            }
            form.Controls.Add(tabs);
            form.Show();
            Application.DoEvents();

            _sb.AppendLine("-- A. 样式参数 --");
            Judge("A1 自绘模式（才能控制标签之间的间隔）",
                  tabs.DrawMode == TabDrawMode.OwnerDrawFixed, "实际 " + tabs.DrawMode);
            Judge("A2 固定尺寸模式（保证所有标签宽度一致）",
                  tabs.SizeMode == TabSizeMode.Fixed, "实际 " + tabs.SizeMode);
            Judge("A3 单行显示（不换行，超出用滚动箭头）", !tabs.Multiline, "Multiline=" + tabs.Multiline);
            Judge("A4 开启悬停提示（看完整标题）", tabs.ShowToolTips, "ShowToolTips=" + tabs.ShowToolTips);
            Judge("A5 标签尺寸 = " + TabStrip.TabWidth + "×" + TabStrip.TabHeight,
                  tabs.ItemSize.Width == TabStrip.TabWidth && tabs.ItemSize.Height == TabStrip.TabHeight,
                  "实际 " + tabs.ItemSize.Width + "×" + tabs.ItemSize.Height);
            Judge("A6 标题字号 = " + TabStrip.TitleSize + "pt（默认 9pt）",
                  Math.Abs(TabStrip.TabTitleFont.Size - TabStrip.TitleSize) < 0.01f,
                  "实际 " + TabStrip.TabTitleFont.Size + "pt");
            Judge("A7 标题字号确实大于默认 9pt", TabStrip.TabTitleFont.Size > 9f,
                  "实际 " + TabStrip.TabTitleFont.Size + "pt");

            _sb.AppendLine();
            _sb.AppendLine("-- B. 所有标签尺寸一致 --");
            bool uniform = true;
            int w0 = tabs.GetTabRect(0).Width;
            int h0 = tabs.GetTabRect(0).Height;
            for (int i = 1; i < tabs.TabPages.Count; i++)
            {
                Rectangle r = tabs.GetTabRect(i);
                if (r.Width != w0 || r.Height != h0) uniform = false;
                Info("标签 " + i + " 区域: " + r.Width + "×" + r.Height + "，标题: " +
                     (tabs.TabPages[i].Text.Length == 0 ? "(空)" : tabs.TabPages[i].Text));
            }
            Judge("B1 每个标签的宽度/高度完全相同", uniform, "标签 0 = " + w0 + "×" + h0);
            Judge("B2 标签宽度等于 ItemSize 宽度", w0 == TabStrip.TabWidth, "实际 " + w0);

            _sb.AppendLine();
            _sb.AppendLine("-- C. 间隔 --");
            Rectangle r0 = tabs.GetTabRect(0);
            Rectangle r1 = tabs.GetTabRect(1);
            Info("相邻标签矩形: x=" + r0.X + " w=" + r0.Width + " → x=" + r1.X + " w=" + r1.Width);
            Judge("C1 相邻标签矩形无重叠", r1.X >= r0.X + r0.Width, "r0.X+w=" + (r0.X + r0.Width) + " vs r1.X=" + r1.X);
            Judge("C2 自绘会在标签内预留间隔（GapX=" + TabStrip.GapX + "）", TabStrip.GapX >= 6, "");

            _sb.AppendLine();
            _sb.AppendLine("-- D. 渲染截图 --");
            bool shotOk = false;
            try
            {
                using (var bmp = new Bitmap(tabs.Width, Math.Min(140, tabs.Height)))
                {
                    tabs.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(shot, ImageFormat.Png);
                    shotOk = File.Exists(shot);
                }
            }
            catch (Exception ex) { Info("截图失败: " + ex.Message); }
            Judge("D1 已输出标签条截图（供人工核对观感）", shotOk, shot);
            if (shotOk) Info("截图: " + shot);

            form.Close();
            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            try { File.WriteAllText(log, _sb.ToString(), Encoding.UTF8); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            _sb.AppendLine("       * " + s);
        }
    }
}
