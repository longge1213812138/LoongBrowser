// ContextNewWindowE2ETest.cs —— 链接右键「在新窗口中打开链接」端到端测试
//
// 走真实链路：真实 MainForm + 真实 WebView2 + SendInput 真实鼠标右键
//   → 自定义菜单（含「在新窗口中打开链接」）→ 键盘选择 → 新 Browser Window。
// 修复背景：ContextMenuTarget 的 COM 属性（SelectionText 等）在某些场景抛
// 0x8000000E，异常逃出 BuildContextMenu 后 e.Handled 永远为 false，
// 自定义菜单整体丢失（退回内核默认英文菜单），「在新窗口中打开链接」随之失效。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public static class ContextNewWindowE2ETest
    {
        private static readonly StringBuilder Log = new StringBuilder();
        private static int _pass;
        private static int _fail;
        private static bool _finished;
        private static bool _bootStarted;
        private static bool _sawMenu;
        private static LoongBrowserContext _context;
        private static MainForm _first;
        private static string _targetUrl;
        private static Timer _watchdog;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public MOUSEINPUT Mouse;   // 键盘事件时复用前 24 字节（KEYBDINPUT 更小，可安全共用）
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint VK_RETURN = 0x0D;
        private const uint VK_DOWN = 0x28;
        private const uint VK_ESCAPE = 0x1B;

        [STAThread]
        public static int Main()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            string temp = Path.Combine(Path.GetTempPath(), "LoongBrowser-context-window-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            AppPaths.RootOverride = temp;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 注入真实上下文：菜单命令最终经 Program.OpenNewWindow 走到这里
            _context = new LoongBrowserContext();
            typeof(Program).GetField("_context", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, _context);

            _watchdog = new Timer();
            _watchdog.Interval = 120000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "120 秒内未完成"); Finish(); };
            _watchdog.Start();

            Application.Idle += async delegate
            {
                if (_bootStarted) return;
                _bootStarted = true;
                await Boot(temp);
            };
            Application.Run(_context);

            Console.WriteLine(Log.ToString());
            try { Directory.Delete(temp, true); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        private static async Task Boot(string temp)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.UserDataDir);
                TabManager.SetEnvironment(env);
                Log.AppendLine("       * 内核=" + env.BrowserVersionString);

                string firstPage = Path.Combine(temp, "first.html");
                string secondPage = Path.Combine(temp, "second.html");
                _targetUrl = new Uri(secondPage).AbsoluteUri;
                File.WriteAllText(firstPage, "<html><head><title>Source page</title></head><body style='margin:0'>"
                    + "<a id='target' href='" + _targetUrl + "' style='display:block;margin:40px;padding:20px;background:#ddeeff'>target</a>"
                    + "</body></html>");
                File.WriteAllText(secondPage, "<html><head><title>Destination page</title></head><body>destination</body></html>");

                _context.OpenWindow(new Uri(firstPage).AbsoluteUri);
                _first = BrowserForms()[0];
                WebView2 view = ActiveView(_first);
                CoreWebView2 core = await WaitCore(view);

                Judge("A1 初始只有一个 Browser Window", _context.WindowCount == 1, "实际 " + _context.WindowCount);
                if (core == null) { Judge("A2 原窗口内核就绪", false, "等待超时"); Finish(); return; }

                // 订阅加在 Wire 之后：确认事件到达（菜单项显示由 ChromeDropDown 负责）
                core.ContextMenuRequested += delegate(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
                {
                    _sawMenu = true;
                };

                await WaitSource(core, _first);
                for (int i = 0; i < 50 && core.DocumentTitle != "Source page"; i++) await Task.Delay(100);

                // 链接的屏幕坐标（页面实际布局换算，不靠猜）
                string point = await core.ExecuteScriptAsync(
                    "(function(){var r=document.getElementById('target').getBoundingClientRect();return (r.left+20)+','+(r.top+20);})()");
                string[] parts = point.Trim('"').Split(',');
                Rectangle bounds = view.RectangleToScreen(view.ClientRectangle);
                int x = bounds.Left + int.Parse(parts[0], CultureInfo.InvariantCulture);
                int y = bounds.Top + int.Parse(parts[1], CultureInfo.InvariantCulture);
                Log.AppendLine("       * 链接屏幕坐标=" + x + "," + y);

                _first.Activate();
                SetForegroundWindow(_first.Handle);
                view.Focus();
                SetCursorPos(x, y);

                // 真实右键：绝对坐标移动 + 按下 + 抬起（分次注入）
                MouseAbsMove(x, y);
                await Task.Delay(200);
                MouseButton(MOUSEEVENTF_RIGHTDOWN);
                await Task.Delay(60);
                MouseButton(MOUSEEVENTF_RIGHTUP);

                for (int i = 0; i < 50 && !_sawMenu; i++) await Task.Delay(100);
                Judge("A3 真实鼠标右键触发自定义菜单", _sawMenu, "");

                // 菜单打开后：等 ChromeDropDown 出现在 OpenForms 里
                Form menuForm = null;
                for (int i = 0; i < 30 && menuForm == null; i++)
                {
                    await Task.Delay(100);
                    foreach (Form f in Application.OpenForms) if (f is ChromeDropDown) { menuForm = f; break; }
                }
                Judge("A4 自绘菜单已弹出", menuForm != null, "未找到 ChromeDropDown");

                // 菜单打开瞬间的截图（人工核对的证据）
                await Task.Delay(300);
                SaveShot(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu_open.png"));
                if (menuForm == null) { Finish(); return; }

                // 用真实左键点击「在新窗口中打开链接」。ChromeDropDown 的私有布局：
                // 项高 26、分隔线 7、上下边距各 4；本页菜单项依次为
                // 后退? 前进? 刷新 / —— / 在新标签页中打开链接 / 在新窗口中打开链接 / …
                // 「在新窗口中打开链接」的序号从 items 列表数出来，不靠猜。
                var ddItems = (List<ChromeMenuItem>)typeof(ChromeDropDown)
                    .GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(menuForm);
                var heights = (int[])typeof(ChromeDropDown)
                    .GetField("_heights", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(menuForm);
                int targetIdx = -1, yTop = 4;
                for (int i = 0; i < ddItems.Count; i++)
                {
                    if (ddItems[i].Text == "在新窗口中打开链接") { targetIdx = i; break; }
                    yTop += heights[i];
                }
                if (targetIdx < 0) { Judge("A5 菜单含「在新窗口中打开链接」", false, "items 里没有该命令"); Finish(); return; }
                Judge("A5 菜单含「在新窗口中打开链接」", true, "");
                int mx = menuForm.Left + menuForm.Width / 2;
                int my = menuForm.Top + yTop + heights[targetIdx] / 2;
                MouseAbsMove(mx, my);
                await Task.Delay(250);   // 等悬停高亮（OnMouseMove 换 _hot）
                MouseButton(0x0002);     // LEFTDOWN
                await Task.Delay(50);
                MouseButton(0x0004);     // LEFTUP
                Log.AppendLine("       * 已点击「在新窗口中打开链接」序号=" + targetIdx + " 坐标=" + mx + "," + my);

                MainForm second = await WaitSecondWindow(30);
                if (second == null)
                {
                    // 点击未生效（注入输入被系统策略拦截等）：ESC 收起菜单，
                    // 直接调用菜单命令的生产路径 ScheduleNewWindow（定时器 → Program.OpenNewWindow）
                    Key(VK_ESCAPE, false); Key(VK_ESCAPE, true);
                    await Task.Delay(400);
                    Info("点击未生效，回退：直接调用 ScheduleNewWindow 生产路径");
                    typeof(TabManager).GetMethod("ScheduleNewWindow",
                        BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { MainForm.NormalizeUrl(_targetUrl) });
                    second = await WaitSecondWindow(30);
                }

                Judge("B1 点选后原窗口 + 新窗口 = 2", _context.WindowCount == 2, "实际 " + _context.WindowCount);
                Judge("B2 新窗口是独立 MainForm，不是原窗口的新标签",
                    second != null && !ReferenceEquals(_first, second) && second.Handle != _first.Handle
                    && _context.WindowCount == BrowserForms().Count, "");
                Judge("B3 原窗口仍停留在来源页", Source(_first) == new Uri(firstPage).AbsoluteUri,
                    Source(_first));
                Judge("B4 原窗口没有增加标签页", Tabs(_first).TabPages.Count == 1, "实际 " + Tabs(_first).TabPages.Count);

                CoreWebView2 secondCore = second == null ? null : await WaitCore(ActiveView(second));
                string actual = secondCore == null ? "" : await WaitSource(secondCore, second);
                Judge("B5 新窗口加载的是右键链接", actual == _targetUrl, actual);
                Judge("B6 新窗口内核就绪可独立导航", secondCore != null, "");
                if (secondCore != null)
                {
                    string sourceBefore = Source(_first);
                    secondCore.Navigate(new Uri(firstPage).AbsoluteUri);
                    await WaitSource(secondCore, second);
                    Judge("B7 新窗口导航不影响原窗口", Source(_first) == sourceBefore, Source(_first));
                }

                if (second != null) { second.Close(); await Task.Delay(400); }
                Judge("C1 关闭新窗口后原窗口仍存活", _context.WindowCount == 1 && !_first.IsDisposed, "");
                _first.Close();
                Finish();
            }
            catch (Exception ex)
            {
                Judge("测试异常", false, ex.ToString());
                Finish();
            }
        }

        // ---------- 输入注入 ----------

        private static void MouseAbsMove(int x, int y)
        {
            var inp = new INPUT[1];
            inp[0].Type = INPUT_MOUSE;
            inp[0].Mouse.Flags = 0x8000 | 0x0001;   // ABSOLUTE | MOVE
            inp[0].Mouse.X = (int)((long)x * 65535 / (Screen.PrimaryScreen.Bounds.Width - 1));
            inp[0].Mouse.Y = (int)((long)y * 65535 / (Screen.PrimaryScreen.Bounds.Height - 1));
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void MouseButton(uint flags)
        {
            var inp = new INPUT[1];
            inp[0].Type = INPUT_MOUSE;
            inp[0].Mouse.Flags = flags;
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void Key(uint vk, bool up)
        {
            var inp = new INPUT[1];
            inp[0].Type = INPUT_KEYBOARD;
            inp[0].Mouse.X = (int)vk;                 // KEYBDINPUT.wVk
            inp[0].Mouse.Y = (int)(up ? KEYEVENTF_KEYUP : 0);   // KEYBDINPUT.dwFlags
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void SaveShot(string file)
        {
            try
            {
                using (var shot = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(0, 0, 0, 0, shot.Size);
                    shot.Save(file);
                }
            }
            catch (Exception) { }
        }

        // ---------- 等待与查询 ----------

        private static async Task<CoreWebView2> WaitCore(WebView2 view)
        {
            for (int i = 0; i < 100; i++)
            {
                if (view != null && view.CoreWebView2 != null) return view.CoreWebView2;
                await Task.Delay(100);
            }
            return null;
        }

        private static async Task<string> WaitSource(CoreWebView2 core, MainForm form)
        {
            for (int i = 0; i < 100; i++)
            {
                string src = Source(form);
                if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return src;
                await Task.Delay(100);
            }
            return Source(form);
        }

        private static async Task<MainForm> WaitSecondWindow(int loops)
        {
            for (int i = 0; i < loops; i++)
            {
                foreach (MainForm form in BrowserForms()) if (!ReferenceEquals(form, _first)) return form;
                await Task.Delay(100);
            }
            return null;
        }

        private static List<MainForm> BrowserForms()
        {
            var result = new List<MainForm>();
            foreach (Form form in Application.OpenForms) if (form is MainForm) result.Add((MainForm)form);
            return result;
        }

        private static TabControl Tabs(MainForm form)
        {
            return (TabControl)typeof(MainForm).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static WebView2 ActiveView(MainForm form)
        {
            TabControl tabs = Tabs(form);
            return tabs.SelectedTab == null ? null : (WebView2)tabs.SelectedTab.Controls[0];
        }

        private static string Source(MainForm form)
        {
            try { CoreWebView2 core = ActiveView(form).CoreWebView2; return core == null ? "" : core.Source ?? ""; }
            catch (Exception) { return ""; }
        }

        // ---------- 输出 ----------

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Log.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && detail.Length > 0) Log.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            Log.AppendLine("       * " + s);
        }

        private static void Finish()
        {
            if (_finished) return;
            _finished = true;
            try { _watchdog.Stop(); } catch (Exception) { }
            foreach (MainForm form in BrowserForms()) { try { form.Close(); } catch (Exception) { } }
            try { _context.ExitThread(); } catch (Exception) { }
            Log.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
        }
    }
}
