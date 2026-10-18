// TooltipTest.cs —— 工具栏按钮"悬停 1 秒显示名字"的回归测试
//
// 覆盖：
//   A. 延迟常量 = 1000ms；每个功能按钮都带名字；分隔线没有名字
//   B. 悬停不足 1 秒不弹、满 1 秒才弹，且文字就是按钮名字
//   C. 换按钮时旧提示立刻收起、重新计时
//   D. 移出外壳收起；点击后收起
//   E. 位置：挂在按钮上方（下方紧接着标签条，压上去会糊住标签标题）
using System;
using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class TooltipTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            Form form = null;
            try
            {
                form = new MainForm(null);
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(200, 200);
                form.Show();
                Pump(1200);

                GlassChrome chrome = FindChrome(form);
                if (chrome == null) { Console.WriteLine("NO CHROME"); return 2; }

                // 启动时鼠标可能正停在按钮上：先挪开
                SetCursorPos(form.Left + 500, form.Top + 400);
                Pump(200);

                _sb.AppendLine("=== 工具栏悬停提示 ===");
                _sb.AppendLine();

                _sb.AppendLine("-- A. 配置 --");
                Judge("A1 悬停延迟 = 1000ms", GlassChrome.TooltipDelayMs == 1000, GlassChrome.TooltipDelayMs);
                Judge("A2 初始不显示提示", chrome.VisibleTooltip == "", chrome.VisibleTooltip);
                Judge("A3 后退 / 前进 / 刷新 都带名字",
                      Tip(chrome, 0) == "后退" && Tip(chrome, 1) == "前进" && Tip(chrome, 2) == "刷新",
                      Tip(chrome, 0) + " / " + Tip(chrome, 1) + " / " + Tip(chrome, 2));
                Judge("A4 分隔线没有名字", Tip(chrome, 3) == "", "[" + Tip(chrome, 3) + "]");
                Judge("A5 所有非分隔按钮都有名字", AllNamed(chrome), "");

                _sb.AppendLine();
                _sb.AppendLine("-- B. 1 秒阈值 --");
                // 先把鼠标挪到窗口中央让热点清零，再挪到按钮上 —— 保证计时器从零开始
                SetCursorPos(form.Left + 500, form.Top + 400);
                Pump(250);
                Hover(chrome, 0);
                Pump(300);
                Judge("B1 悬停 300ms 不显示", chrome.VisibleTooltip == "", chrome.VisibleTooltip);
                Pump(900);
                Judge("B2 悬停满 1.2s 显示", chrome.VisibleTooltip != "", "空");
                Judge("B3 显示的就是按钮名字", chrome.VisibleTooltip == "后退", chrome.VisibleTooltip);
                Judge("B4 提示窗真的可见", chrome.TooltipVisible, "");

                _sb.AppendLine();
                _sb.AppendLine("-- C. 位置 --");
                Rectangle tip = chrome.TooltipScreenBounds;
                Rectangle btn = ButtonRect(chrome, 0);
                Point btnTop = chrome.PointToScreen(new Point(btn.X, btn.Top));
                Point btnBottom = chrome.PointToScreen(new Point(btn.X, btn.Bottom));
                Point btnRight = chrome.PointToScreen(new Point(btn.Right, btn.Top));
                Judge("C1 提示在按钮右侧（不压住按钮本身）", tip.Left >= btnRight.X,
                      "tip.Left=" + tip.Left + " btnRight=" + btnRight.X);
                Judge("C2 提示竖直落在工具栏内（不压菜单栏、不压标签条）",
                      tip.Top >= chrome.PointToScreen(new Point(0, 0)).Y + 26 &&
                      tip.Bottom <= chrome.PointToScreen(new Point(0, 0)).Y + 62,
                      "tip=" + tip);
                Judge("C3 不越出窗口左右边界",
                      tip.Left >= form.Left && tip.Right <= form.Right,
                      "tip=" + tip + " form=" + form.Bounds);

                _sb.AppendLine();
                _sb.AppendLine("-- D. 切换与收起 --");
                Hover(chrome, 1);
                Pump(150);
                Judge("D1 换按钮后旧提示立刻收起", chrome.VisibleTooltip == "", chrome.VisibleTooltip);
                Pump(1100);
                Judge("D2 新按钮重新计时后显示", chrome.VisibleTooltip == "前进", chrome.VisibleTooltip);

                SetCursorPos(form.Left + 500, form.Top + 400);
                Pump(250);
                Judge("D3 移出外壳后收起", chrome.VisibleTooltip == "" && !chrome.TooltipVisible, chrome.VisibleTooltip);

                Hover(chrome, 2);
                Pump(1150);
                Judge("D4 悬停刷新按钮显示'刷新'", chrome.VisibleTooltip == "刷新", chrome.VisibleTooltip);
                SendMouseDown(chrome, 2);
                Pump(150);
                Judge("D5 点击后收起提示", chrome.VisibleTooltip == "", chrome.VisibleTooltip);

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

        private static GlassChrome FindChrome(Form form)
        {
            foreach (Control c in form.Controls)
                if (c is GlassChrome) return (GlassChrome)c;
            return null;
        }

        private static IList ButtonList(GlassChrome c)
        {
            return (IList)typeof(GlassChrome)
                .GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
        }

        private static IList RectList(GlassChrome c)
        {
            return (IList)typeof(GlassChrome)
                .GetField("_buttonRects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
        }

        private static string Tip(GlassChrome c, int i)
        {
            IList l = ButtonList(c);
            if (i >= l.Count) return "";
            ChromeToolButton b = (ChromeToolButton)l[i];
            return b.Separator ? "" : (b.Tooltip ?? "");
        }

        private static bool AllNamed(GlassChrome c)
        {
            IList l = ButtonList(c);
            foreach (object o in l)
            {
                ChromeToolButton b = (ChromeToolButton)o;
                if (!b.Separator && string.IsNullOrEmpty(b.Tooltip)) return false;
            }
            return true;
        }

        private static Rectangle ButtonRect(GlassChrome c, int i)
        {
            return (Rectangle)RectList(c)[i];
        }

        private static void Hover(GlassChrome c, int i)
        {
            Rectangle r = ButtonRect(c, i);
            Point p = c.PointToScreen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
            SetCursorPos(p.X, p.Y);
        }

        /// <summary>直接在控件上模拟一次左键按下（不移动真实鼠标）。</summary>
        private static void SendMouseDown(GlassChrome c, int i)
        {
            Rectangle r = ButtonRect(c, i);
            MethodInfo m = typeof(Control).GetMethod("OnMouseDown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            m.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                r.X + r.Width / 2, r.Y + r.Height / 2, 0) });
        }

        private static void Judge(string name, bool ok, object detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && detail != null) _sb.AppendLine("       " + detail);
        }

        private static void Pump(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}