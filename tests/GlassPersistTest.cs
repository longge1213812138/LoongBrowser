// GlassPersistTest.cs —— 玻璃常驻回归（锁定"失焦丢玻璃"这个 bug）
//
// 症状（用户报告）：点工具/书签等菜单、切换到其他应用、甚至点一下桌面，毛玻璃就消失。
// 根因（二分定位，2026-10）：窗口补了 WS_THICKFRAME 之后，DWM 在窗口失活时停用 Acrylic，
//   整个窗口变成不透明纯色块；重新 Apply / 强制重绘 / 重设边框都无法恢复。
//   对照实验：同一窗口去掉 WS_THICKFRAME，失焦后玻璃完好。
//
// 这个测试锁两件事：
//   A. 窗口样式里不能有 WS_THICKFRAME（有它则失焦必丢玻璃）
//   B. 边缘缩放仍然可用 —— 改为 WindowResizer 自己实现，不依赖系统缩放循环
using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class GlassPersistTest
    {
        private const int WS_THICKFRAME = 0x00040000;

        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            Form form = null;
            try
            {
                _sb.AppendLine("=== 玻璃常驻回归：WS_THICKFRAME 与失焦 ===");
                _sb.AppendLine();

                form = new MainForm(null);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                Application.DoEvents();

                // ---------- A. 窗口样式 ----------
                _sb.AppendLine("-- A. 窗口样式（决定 DWM 是否在失焦时保留 Acrylic） --");
                CreateParams cp = (CreateParams)typeof(Form)
                    .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form, null);

                Judge("A1 窗口样式不含 WS_THICKFRAME（含它则失焦必丢玻璃）",
                      (cp.Style & WS_THICKFRAME) == 0,
                      "style=0x" + cp.Style.ToString("X8"));
                Judge("A2 仍保留最小化按钮语义", (cp.Style & WindowResizer.WS_MINIMIZEBOX) != 0, "");
                Judge("A3 仍保留最大化按钮语义", (cp.Style & WindowResizer.WS_MAXIMIZEBOX) != 0, "");

                // ---------- B. 边缘命中 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- B. 边缘命中（自实现缩放） --");
                var size = new Size(1000, 700);
                Judge("B1 左边中 → Edge.Left", Hit(0, 350, size) == WindowResizer.Edge.Left, Hit(0, 350, size));
                Judge("B2 右边中 → Edge.Right", Hit(999, 350, size) == WindowResizer.Edge.Right, Hit(999, 350, size));
                Judge("B3 上边中 → Edge.Top", Hit(500, 0, size) == WindowResizer.Edge.Top, Hit(500, 0, size));
                Judge("B4 下边中 → Edge.Bottom", Hit(500, 699, size) == WindowResizer.Edge.Bottom, Hit(500, 699, size));
                Judge("B5 左上角 → Top|Left",
                      Hit(0, 0, size) == (WindowResizer.Edge.Top | WindowResizer.Edge.Left), Hit(0, 0, size));
                Judge("B6 右下角 → Bottom|Right",
                      Hit(999, 699, size) == (WindowResizer.Edge.Bottom | WindowResizer.Edge.Right), Hit(999, 699, size));
                Judge("B7 内部 → Edge.None", Hit(500, 350, size) == WindowResizer.Edge.None, Hit(500, 350, size));
                Judge("B8 窄窗口不判为边缘（否则内部控件全不可点）",
                      WindowResizer.HitTest(new Point(0, 10), new Size(23, 400)) == WindowResizer.Edge.None, "");
                Judge("B9 边缘带宽 = VistaTheme.ResizeBorderWidth",
                      VistaTheme.ResizeBorderWidth == 6, VistaTheme.ResizeBorderWidth);

                // ---------- C. 一次拖拽真的改变矩形 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- C. 拖拽会话（直接作用于窗口 Bounds） --");
                form.Bounds = new Rectangle(100, 100, 800, 600);

                Cursor.Position = new Point(100, 400);
                var left = new WindowResizer.DragSession(
                    form, WindowResizer.Edge.Left, form.Bounds, new Point(100, 400), new Size(300, 200));
                left.Update(new Point(40, 400));               // 左移 60
                Judge("C1 拖左边 60px → 宽度 +60、左沿 -60",
                      form.Bounds.Width == 860 && form.Bounds.Left == 40, form.Bounds.ToString());

                var right = new WindowResizer.DragSession(
                    form, WindowResizer.Edge.Right, form.Bounds, new Point(0, 400), new Size(300, 200));
                right.Update(new Point(120, 400));             // 右移 120
                Judge("C2 拖右边 120px → 宽度 +120、左沿不变",
                      form.Bounds.Width == 980 && form.Bounds.Left == 40, form.Bounds.ToString());

                // 最小尺寸钳制：往左拖到宽度低于 MinimumSize
                var clamp = new WindowResizer.DragSession(
                    form, WindowResizer.Edge.Right, form.Bounds, new Point(0, 400), form.MinimumSize);
                clamp.Update(new Point(-5000, 400));
                Judge("C3 宽度被 MinimumSize 钳住", form.Bounds.Width == form.MinimumSize.Width,
                      form.Bounds.Width + " vs " + form.MinimumSize.Width);

                // 向上拖到高度低于 MinimumSize
                form.Bounds = new Rectangle(100, 100, 900, 700);
                var clampV = new WindowResizer.DragSession(
                    form, WindowResizer.Edge.Bottom, form.Bounds, new Point(0, 0), form.MinimumSize);
                clampV.Update(new Point(0, -5000));
                Judge("C4 高度被 MinimumSize 钳住", form.Bounds.Height == form.MinimumSize.Height,
                      form.Bounds.Height + " vs " + form.MinimumSize.Height);

                // 角落：同时改宽高
                form.Bounds = new Rectangle(100, 100, 900, 700);
                var corner = new WindowResizer.DragSession(
                    form, WindowResizer.Edge.Bottom | WindowResizer.Edge.Right,
                    form.Bounds, new Point(0, 0), new Size(300, 200));
                corner.Update(new Point(50, 40));
                Judge("C5 右下角同时改宽高",
                      form.Bounds.Width == 950 && form.Bounds.Height == 740, form.Bounds.ToString());

                form.Close();
            }
            catch (Exception ex)
            {
                _fail++;
                _sb.AppendLine("[FAIL] 未捕获异常：" + ex);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    try { form.Close(); } catch (Exception) { }
                    try { form.Dispose(); } catch (Exception) { }
                }
            }

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static WindowResizer.Edge Hit(int x, int y, Size size)
        {
            return WindowResizer.HitTest(new Point(x, y), size);
        }

        private static void Judge(string name, bool ok, object detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && detail != null) _sb.AppendLine("       " + detail);
        }
    }
}
