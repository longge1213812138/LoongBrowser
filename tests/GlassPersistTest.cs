// GlassPersistTest.cs —— 玻璃常驻回归（锁定"失焦丢玻璃"这个 bug）
//
// 症状（用户报告）：点菜单、切到其他应用、甚至点一下桌面，毛玻璃就消失。
// 根因（复测于 2026-10，见 docs/adr/0010 与 tests\GlassMatrix.cs）：
//   只要窗口样式里出现 WS_THICKFRAME，DWM 就会在窗口失焦时停用 Acrylic；
//   失焦后重新 Apply / Clear+Apply / 强制重绘都救不回来（sat 恒为 0，回前台才恢复）。
//   四种边框形态（None / Sizable / NCCALCSIZE 归零）都绕不过去。
//
// 所以这个测试锁的是那条不能碰的红线：
//   A. 窗口样式里不能有 WS_THICKFRAME；
//   B. 缩放/分屏改用不依赖它的方案（WM_NCHITTEST + SnapController），能力不能少。
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

                // ---------- B. 不靠系统样式，能力也不能少 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- B. 缩放与分屏能力（不依赖 WS_THICKFRAME） --");
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

                // 边缘 → HT 值：系统据此启动真正的缩放循环
                Judge("B10 左边 → HTLEFT", WindowResizer.HitTestCode(WindowResizer.Edge.Left) == WindowResizer.HTLEFT, "");
                Judge("B11 右下角 → HTBOTTOMRIGHT",
                      WindowResizer.HitTestCode(WindowResizer.Edge.Bottom | WindowResizer.Edge.Right)
                          == WindowResizer.HTBOTTOMRIGHT, "");

                // 贴边分屏（自实现，见 SnapController）
                var work = new Rectangle(0, 0, 1920, 1040);
                Judge("B12 贴左边缘 → 左半屏",
                      SnapController.ZoneAt(new Point(2, 500), work) == SnapController.Zone.Left, "");
                Judge("B13 左半屏宽度 = 一半",
                      SnapController.BoundsFor(SnapController.Zone.Left, work).Width == 960, "");
                Judge("B14 贴顶 → 最大化",
                      SnapController.ZoneAt(new Point(900, 2), work) == SnapController.Zone.Maximize, "");

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
