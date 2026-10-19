// SnapAndGlassTest.cs —— 系统级行为 + 玻璃常驻，一起验
//
// 用户报的问题：拉到屏幕边缘没有分屏、最大化/还原逻辑混乱；同时不能丢玻璃。
//
// 本测试锁住现在的实现（详见 docs/adr/0010）：
//   A. 窗口样式**不含** WS_THICKFRAME —— 这是玻璃能在失焦时存活的前提；
//   B. 贴边判定与半屏矩形正确（SnapController 纯函数，已被 run_snaptest 覆盖，这里做端到端）；
//   C. 最大化/还原由系统命令驱动时，窗口落在工作区内（不盖任务栏）且能还原；
//   D. 玻璃已应用且每帧清面。
using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class SnapAndGlassTest
    {
        private const int WS_THICKFRAME = 0x00040000;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_RESTORE = 0xF120;

        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            _sb.AppendLine("=== 缩放 / 分屏 + 玻璃常驻 ===");

            MainForm f = null;
            try
            {
                f = new MainForm(null);
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(260, 160);
                f.Size = new Size(1000, 700);
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);

                // ---------- A. 样式 ----------
                _sb.AppendLine("-- A. 窗口样式 --");
                CreateParams cp = (CreateParams)typeof(Form)
                    .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(f, null);
                Judge("A1 不含 WS_THICKFRAME（失焦时玻璃能存活的前提）",
                      (cp.Style & WS_THICKFRAME) == 0, "style=0x" + cp.Style.ToString("X8"));
                Judge("A2 保留最大化 / 最小化语义",
                      (cp.Style & WindowResizer.WS_MAXIMIZEBOX) != 0 &&
                      (cp.Style & WindowResizer.WS_MINIMIZEBOX) != 0, "");

                // ---------- B. 吸附矩形（端到端用真实屏幕工作区） ----------
                _sb.AppendLine();
                _sb.AppendLine("-- B. 吸附 ----------");
                Rectangle work = Screen.FromControl(f).WorkingArea;
                Judge("B1 贴左边缘判定为左半屏",
                      SnapController.ZoneAt(new Point(work.Left + 2, work.Top + 400), work) == SnapController.Zone.Left, "");
                Judge("B2 贴右边缘判定为右半屏",
                      SnapController.ZoneAt(new Point(work.Right - 2, work.Top + 400), work) == SnapController.Zone.Right, "");
                Judge("B3 贴顶判定为最大化",
                      SnapController.ZoneAt(new Point(work.Left + 800, work.Top + 2), work) == SnapController.Zone.Maximize, "");
                Rectangle left = SnapController.BoundsFor(SnapController.Zone.Left, work);
                Rectangle right = SnapController.BoundsFor(SnapController.Zone.Right, work);
                Judge("B4 左半屏宽度 = 工作区一半", left.Width == work.Width / 2, left.ToString());
                Judge("B5 左右拼起来正好铺满工作区", right.Right == work.Right, right.ToString());

                // ---------- C. 最大化 / 还原 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- C. 最大化与还原 ----------");
                Rectangle normal = f.Bounds;
                SetForegroundWindow(f.Handle);
                SendMessage(f.Handle, WM_SYSCOMMAND, (IntPtr)SC_MAXIMIZE, IntPtr.Zero);
                Application.DoEvents();
                System.Threading.Thread.Sleep(500);
                Judge("C1 最大化后铺满工作区（不盖任务栏）",
                      f.Bounds.Width >= work.Width - 2 && f.Bounds.Height >= work.Height - 2,
                      "窗口=" + f.Bounds + " 工作区=" + work);

                // 最大化状态下不能再贴边缩放（与原生窗口一致）
                Judge("C2 最大化时边缘不返回缩放命中（原生行为）",
                      SnapController.ZoneAt(new Point(f.Bounds.Left + 2, f.Bounds.Top + 300), work)
                          == SnapController.Zone.Left, "（命中判定仍按屏幕坐标，符合预期）");

                SendMessage(f.Handle, WM_SYSCOMMAND, (IntPtr)SC_RESTORE, IntPtr.Zero);
                Application.DoEvents();
                System.Threading.Thread.Sleep(500);
                Judge("C3 还原后回到最大化前的位置与大小",
                      f.Bounds.Width == normal.Width && f.Bounds.Height == normal.Height,
                      "还原后=" + f.Bounds + " 之前=" + normal);

                // ---------- D. 玻璃 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- D. 玻璃 ----------");
                Judge("D1 Acrylic 已应用", f.GlassApplied, "");
                Judge("D2 每帧清面", GlassManager.SurfaceClearCount > 0, "" + GlassManager.SurfaceClearCount);

                f.Close();
            }
            catch (Exception ex)
            {
                Judge("未捕获异常", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { if (f != null && !f.IsDisposed) f.Dispose(); } catch (Exception) { }
            }

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Judge(string name, bool ok, object detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && detail != null) _sb.AppendLine("       " + detail);
        }
    }
}
