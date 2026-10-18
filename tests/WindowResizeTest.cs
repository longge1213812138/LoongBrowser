// WindowResizeTest.cs —— 无边框窗口边缘缩放的回归测试（不注入真实鼠标）
//
// 覆盖：
//   A. 命中测试：八向边缘/角落命中正确，内部区域仍是 HTCLIENT，窄窗口不误判
//   B. 窗口样式：CreateParams 里必须有 WS_THICKFRAME（否则系统不执行缩放）
//   C. 客户区：WM_NCCALCSIZE 归零后 ClientSize 等于窗口尺寸（不被边框挤小）
//   D. 边带布局：内容控件缩在 Padding 内侧，边缘一圈留给缩放
//   E. 最小尺寸：ptMinTrackSize 用的是 MinimumSize，不是系统默认值
//   F. 最大化：命中测试不再报缩放码；窗口矩形落在工作区内
using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class WindowResizeTest
    {
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
                _sb.AppendLine("=== 无边框窗口边缘缩放回归 ===");
                _sb.AppendLine();

                int band = VistaTheme.ResizeBorderWidth;
                Info("边缘带宽 = " + band + "px");

                // ---------- A. 命中测试 ----------
                _sb.AppendLine("-- A. 命中测试 --");
                var size = new Size(1000, 700);
                Judge("A1 左上角 → HTTOPLEFT", Hit(0, 0, size) == WindowResizer.HTTOPLEFT, Hit(0, 0, size));
                Judge("A2 上边中 → HTTOP", Hit(500, 0, size) == WindowResizer.HTTOP, Hit(500, 0, size));
                Judge("A3 右上角 → HTTOPRIGHT", Hit(size.Width - 1, 0, size) == WindowResizer.HTTOPRIGHT, Hit(size.Width - 1, 0, size));
                Judge("A4 右边中 → HTRIGHT", Hit(size.Width - 1, 350, size) == WindowResizer.HTRIGHT, Hit(size.Width - 1, 350, size));
                Judge("A5 右下角 → HTBOTTOMRIGHT", Hit(size.Width - 1, size.Height - 1, size) == WindowResizer.HTBOTTOMRIGHT, Hit(size.Width - 1, size.Height - 1, size));
                Judge("A6 下边中 → HTBOTTOM", Hit(500, size.Height - 1, size) == WindowResizer.HTBOTTOM, Hit(500, size.Height - 1, size));
                Judge("A7 左下角 → HTBOTTOMLEFT", Hit(0, size.Height - 1, size) == WindowResizer.HTBOTTOMLEFT, Hit(0, size.Height - 1, size));
                Judge("A8 左边中 → HTLEFT", Hit(0, 350, size) == WindowResizer.HTLEFT, Hit(0, 350, size));
                Judge("A9 带内最后一个像素仍是边上（x=band-1）", Hit(band - 1, 350, size) == WindowResizer.HTLEFT, Hit(band - 1, 350, size));
                Judge("A10 带外第一个像素是客户区（x=band）", Hit(band, 350, size) == WindowResizer.HTCLIENT, Hit(band, 350, size));
                Judge("A11 正中间 → HTCLIENT", Hit(500, 350, size) == WindowResizer.HTCLIENT, Hit(500, 350, size));
                Judge("A12 窄窗口（宽 < 4×带）不判边缘，避免控件全不可点",
                      Hit(0, 10, new Size(band * 4 - 1, 400)) == WindowResizer.HTCLIENT,
                      Hit(0, 10, new Size(band * 4 - 1, 400)));

                // ---------- B/C/D. 真实窗口 ----------
                form = new MainForm(null);
                form.Opacity = 0;                       // 测试不干扰用户桌面
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                Application.DoEvents();

                _sb.AppendLine();
                _sb.AppendLine("-- B. 窗口样式 --");
                CreateParams cp = (CreateParams)typeof(Form)
                    .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form, null);
                Judge("B1 带 WS_THICKFRAME（系统据此才执行缩放）",
                      (cp.Style & WindowResizer.WS_THICKFRAME) != 0,
                      "style=0x" + cp.Style.ToString("X8"));
                Judge("B2 保留 WS_MINIMIZEBOX", (cp.Style & WindowResizer.WS_MINIMIZEBOX) != 0, "");
                Judge("B3 保留 WS_MAXIMIZEBOX", (cp.Style & WindowResizer.WS_MAXIMIZEBOX) != 0, "");

                _sb.AppendLine();
                _sb.AppendLine("-- C. 客户区 = 窗口矩形 --");
                Judge("C1 ClientSize 等于 Size（NCCALCSIZE 已把边框归零）",
                      form.ClientSize == form.Size, "ClientSize=" + form.ClientSize + " Size=" + form.Size);
                Judge("C2 窗口尺寸仍受 MinimumSize 约束",
                      form.Size.Width >= form.MinimumSize.Width && form.Size.Height >= form.MinimumSize.Height,
                      form.Size + " vs " + form.MinimumSize);

                _sb.AppendLine();
                _sb.AppendLine("-- D. 边带布局（内容缩在 Padding 内） --");
                int pad = VistaTheme.ResizeBorderWidth;
                Judge("D1 Form.Padding 四周都等于边缘带宽",
                      form.Padding.Left == pad && form.Padding.Top == pad &&
                      form.Padding.Right == pad && form.Padding.Bottom == pad, form.Padding.ToString());

                Control titleBar = null, chrome = null, tabs = null;
                foreach (Control c in form.Controls)
                {
                    if (c is TitleBar) titleBar = c;
                    else if (c is GlassChrome) chrome = c;
                    else if (c is TabControl) tabs = c;
                }
                Judge("D2 标题栏左沿从 Padding 内侧开始", titleBar != null && titleBar.Left == pad,
                      titleBar == null ? "未找到 TitleBar" : "Left=" + titleBar.Left);
                Judge("D3 标题栏顶沿从 Padding 内侧开始", titleBar != null && titleBar.Top == pad,
                      titleBar == null ? "" : "Top=" + titleBar.Top);
                Judge("D4 外壳与标题栏同宽（右侧也留出边带）",
                      chrome != null && titleBar != null && chrome.Width == titleBar.Width,
                      chrome == null ? "未找到 GlassChrome" : chrome.Width + " vs " + titleBar.Width);
                Judge("D5 内容区右沿止于 Padding 内侧",
                      tabs != null && tabs.Right == form.ClientSize.Width - pad,
                      tabs == null ? "未找到 TabControl" : "Right=" + tabs.Right + " 期望 " + (form.ClientSize.Width - pad));

                _sb.AppendLine();
                _sb.AppendLine("-- E. 最小尺寸 --");
                Size min = WindowResizer.MinimumTrackSize(form.MinimumSize);
                Judge("E1 ptMinTrackSize 取自 MinimumSize（不是系统默认 136x39）",
                      min.Width == form.MinimumSize.Width && min.Height == form.MinimumSize.Height,
                      min.ToString() + " vs " + form.MinimumSize);
                Judge("E2 最小尺寸不小于 4×边缘带（否则命中测试会整体退化）",
                      min.Width >= pad * 4 && min.Height >= pad * 4, min.ToString());

                _sb.AppendLine();
                _sb.AppendLine("-- F. 最大化 --");
                form.WindowState = FormWindowState.Maximized;
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();

                Rectangle work = Screen.FromControl(form).WorkingArea;
                Rectangle maxBounds = form.Bounds;
                Judge("F1 最大化矩形落在工作区内（不溢出、不压任务栏）",
                      maxBounds.Left >= work.Left && maxBounds.Top >= work.Top &&
                      maxBounds.Right <= work.Right && maxBounds.Bottom <= work.Bottom,
                      "bounds=" + maxBounds + " work=" + work);
                Judge("F2 最大化时不报缩放码（系统本来也不允许拖边改大小）",
                      HitScreenEdge(form) == WindowResizer.HTCLIENT, HitScreenEdge(form));

                form.WindowState = FormWindowState.Normal;
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();

                Judge("F3 还原后命测试恢复可用",
                      HitScreenEdge(form) == WindowResizer.HTLEFT, HitScreenEdge(form));

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

        private static int Hit(int x, int y, Size size)
        {
            return WindowResizer.HitTest(new Point(x, y), size);
        }

        /// <summary>
        /// 用真实窗口走一次 WM_NCHITTEST（经 WindowResizer.HandleMessage）：
        /// 这样才覆盖到"最大化时禁止缩放"那条分支。返回 m.Result，0 表示未接管（按客户区处理）。
        /// </summary>
        private static int HitScreenEdge(Form f)
        {
            Point screen = f.PointToScreen(new Point(0, f.ClientSize.Height / 2));
            long lParam = ((long)(screen.Y & 0xFFFF) << 16) | (uint)(screen.X & 0xFFFF);
            var m = new Message();
            m.HWnd = f.Handle;
            m.Msg = 0x0084;                       // WM_NCHITTEST
            m.WParam = IntPtr.Zero;
            m.LParam = (IntPtr)lParam;
            bool handled = WindowResizer.HandleMessage(ref m, f);
            if (!handled) return WindowResizer.HTCLIENT;
            return (int)m.Result.ToInt64();
        }

        private static void Judge(string name, bool ok, object detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && detail != null) _sb.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            _sb.AppendLine("       * " + s);
        }
    }
}
