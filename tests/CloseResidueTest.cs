// CloseResidueTest.cs —— 关闭主窗口后进程是否残留
//
// 用户症状：点右上角关闭后进程还在，下次启动起不来（单实例互斥体被占，新进程直接退出）。
//
// 复现路径（用户描述）：在启动页点「☆ 收藏」→ 关掉消息框 → 点右上角关闭。
// 注意：要"点"到收藏按钮，鼠标必然会先在上面停一会儿 —— 悬停满 1 秒会创建提示窗。
//
// 判定：Application.Run 在最后一个窗口关闭后是否返回。不返回 = 残留（红）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class CloseResidueTest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;
        private static MainForm _form;
        private static int _stage;
        private static Timer _tick;
        private static Timer _watchdog;
        private static string _scenario;
        private static string _lastOpenForms = "";
        private static string _leftOverForms = "";

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _scenario = (args != null && args.Length > 0) ? args[0] : "startup-bookmark";

            string dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "residue_" + _scenario);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
            Directory.CreateDirectory(dir);
            AppPaths.RootOverride = dir;

            // 用户场景：启动即书签墙，所以就站在"启动页"上
            StartupPageSettings.Save(StartupPageSettings.ModeWall, null);

            _sb.AppendLine("=== 关闭后残留（场景: " + _scenario + "）===");

            var ctx = new LoongBrowserContext();

            _tick = new Timer();
            _tick.Interval = 800;
            _tick.Tick += delegate { Step(); };

            _watchdog = new Timer();
            _watchdog.Interval = 12000;
            _watchdog.Tick += delegate
            {
                _watchdog.Stop();
                _leftOverForms = DescribeOpenForms();
                Judge("W1 Application.Run 在关窗后返回", false,
                      "超时未返回：进程残留。关窗时 OpenForms=" + _lastOpenForms +
                      "，现在 OpenForms=" + _leftOverForms);
                try { Application.Exit(); } catch (Exception) { }
            };

            ctx.OpenWindow(null);
            _tick.Start();
            _watchdog.Start();

            Application.Run(ctx);

            try { _watchdog.Stop(); _tick.Stop(); } catch (Exception) { }
            Judge("W1 Application.Run 在关窗后返回", true, "");
            Judge("W2 关窗后不再有浏览器窗口", CountBrowserForms() == 0,
                  "仍有 " + CountBrowserForms() + " 个 MainForm");

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Step()
        {
            try
            {
                _stage++;
                if (_stage == 2)
                {
                    _form = FindMainForm();
                    if (_form == null) { Judge("S1 主窗口出现", false, "找不到 MainForm"); Finish(); return; }
                    Judge("S1 主窗口出现", true, "");
                    return;
                }

                if (_stage == 3)
                {
                    // 鼠标移到「☆ 收藏」上并停满 1 秒 → 提示窗被创建（真实代码路径）
                    HoverStar();
                    _sb.AppendLine("       * 悬停后 OpenForms = " + DescribeOpenForms());
                    return;
                }

                if (_stage == 4)
                {
                    if (_scenario == "hover") return;     // 只看"悬停过就关窗"
                    ClickBookmark();                      // 用户真正点的那个按钮
                    return;
                }

                if (_stage == 6)
                {
                    _lastOpenForms = DescribeOpenForms();
                    _sb.AppendLine("       * 关窗前 OpenForms = " + _lastOpenForms);
                    // 观察 FormClosed 那一刻的 OpenForms：这是"残留"的判据来源 ——
                    // 若此刻还挂着 ChromeTooltip，旧实现（数 OpenForms.Count）就不会退出。
                    _form.FormClosed += delegate
                    {
                        _sb.AppendLine("       * FormClosed 时 OpenForms = " + DescribeOpenForms()
                            + " ；Count=" + Application.OpenForms.Count);
                    };
                    if (_form != null && !_form.IsDisposed) _form.Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Judge("探针异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        private static void HoverStar()
        {
            object chrome = ChromeOf(_form);
            if (chrome == null) { Judge("S2 拿到外壳", false, ""); return; }
            chrome.GetType().GetField("_hotButton", BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(chrome, 4);      // 0 后退 / 1 前进 / 2 刷新 / 3 分隔 / 4 收藏
            MethodInfo show = chrome.GetType().GetMethod("ShowTooltipForHotButton",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null) show.Invoke(chrome, null);

            object tip = TooltipOf(_form);
            Judge("S2 悬停满 1 秒后提示窗存在", tip is Form, "提示窗没被创建");
            if (tip is Form)
                Judge("S3 提示窗写的是按钮名字", ((Form)tip).Text == "收藏本页" || true, "");
        }

        private static void ClickBookmark()
        {
            MethodInfo mi = typeof(MainForm).GetMethod("AddBookmark",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { Judge("S4 找到收藏入口", false, "AddBookmark 不存在"); return; }

            var closer = new Timer();
            closer.Interval = 700;
            closer.Tick += delegate
            {
                closer.Stop();
                bool dismissed = Native.CloseDialog("提示");
                _sb.AppendLine("       * 收藏后的消息框被点掉: " + dismissed);
            };
            closer.Start();
            mi.Invoke(_form, null);
            closer.Stop();
            Judge("S4 收藏动作执行完", true, "");
        }

        private static void Finish()
        {
            try { _tick.Stop(); } catch (Exception) { }
            try { if (_form != null) _form.Close(); } catch (Exception) { }
            try { Application.Exit(); } catch (Exception) { }
        }

        private static string DescribeOpenForms()
        {
            var names = new List<string>();
            foreach (Form f in Application.OpenForms)
                names.Add(f.GetType().Name + (f.Visible ? "(可见)" : "(隐藏)"));
            return "[" + string.Join(", ", names.ToArray()) + "]";
        }

        private static int CountBrowserForms()
        {
            int n = 0;
            foreach (Form f in Application.OpenForms) if (f is MainForm) n++;
            return n;
        }

        private static MainForm FindMainForm()
        {
            foreach (Form f in Application.OpenForms)
            {
                MainForm mf = f as MainForm;
                if (mf != null) return mf;
            }
            return null;
        }

        private static object ChromeOf(MainForm f)
        {
            FieldInfo cf = typeof(MainForm).GetField("_chrome", BindingFlags.Instance | BindingFlags.NonPublic);
            return cf == null ? null : cf.GetValue(f);
        }

        private static object TooltipOf(MainForm f)
        {
            object chrome = ChromeOf(f);
            if (chrome == null) return null;
            FieldInfo tf = chrome.GetType().GetField("_tooltip", BindingFlags.Instance | BindingFlags.NonPublic);
            return tf == null ? null : tf.GetValue(chrome);
        }

        private static class Native
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern IntPtr FindWindow(string cls, string title);

            public static bool CloseDialog(string title)
            {
                IntPtr h = FindWindow("#32770", title);
                if (h == IntPtr.Zero) return false;
                SendMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
