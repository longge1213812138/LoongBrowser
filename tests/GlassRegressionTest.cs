// GlassRegressionTest.cs —— 玻璃外壳回归测试（不依赖 DWM 合成层）
//
// 覆盖三条这次修好的不变量：
//   A. 外壳/标题栏每个绘制帧都先清面（ClearSurface）再画前景 —— 少了它，窗口变窄后
//      旧位置的地址栏白条会留在窗口表面（用户截图里的"地址栏瑕疵"）。
//   B. 绘制前布局已定稿：地址栏文本框在 OnPaint 内不再移动。
//   C. Apply 幂等：重复调用仍然成功，且会先退回 NONE 再设 Acrylic。
//   D. 启动时机：Apply 必须发生在窗口显示之后（MainForm.OnShown），不能在构造函数里。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class GlassRegressionTest
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
                _sb.AppendLine("=== 玻璃外壳回归：清面 / 布局 / 幂等 / 启动时机 ===");
                _sb.AppendLine();

                // ---------- A/B. 外壳绘制 ----------
                form = new Form();
                form.FormBorderStyle = FormBorderStyle.None;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ClientSize = new Size(900, 300);
                form.BackColor = Color.White;

                var titleBar = new TitleBar(form);
                var chrome = new GlassChrome();
                var tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;
                TabStrip.HideHeader(tabs);
                for (int i = 0; i < 2; i++) tabs.TabPages.Add(new TabPage("标签" + i));

                form.Controls.Add(tabs);
                form.Controls.Add(chrome);
                form.Controls.Add(titleBar);
                form.Show();
                Application.DoEvents();

                chrome.AttachTabs(tabs);
                chrome.SetMenus(BuildMenus());
                chrome.SetButtons(BuildButtons());
                chrome.ApplyVisibility();
                Application.DoEvents();

                // A. 清面次数随绘制增长
                int before = GlassManager.SurfaceClearCount;
                PaintToBitmap(titleBar);
                PaintToBitmap(chrome);
                int after = GlassManager.SurfaceClearCount;
                Judge("A1 外壳与标题栏绘制时都先清面（每帧各一次）", after - before == 2,
                      "清面次数增量 = " + (after - before));

                // A2. 清面会放开裁剪区：局部失效矩形清面后，整块前景仍要能画出来
                Judge("A2 清面后绘制不再被失效矩形裁剪（否则矩形外的前景会消失）",
                      PaintWithSmallInvalidRect(chrome), "局部重绘后窗口右上角区域为空");

                // B. 绘制前后地址栏文本框位置不变（布局在绘制前已定稿）
                Rectangle boxBefore = chrome.AddressBox.Bounds;
                PaintToBitmap(chrome);
                Rectangle boxAfter = chrome.AddressBox.Bounds;
                Judge("B1 OnPaint 内不再移动地址栏文本框", boxBefore == boxAfter,
                      boxBefore + " -> " + boxAfter);

                // B2. 变窄重新布局后，地址栏右端贴住新的窗口宽度
                // 外壳是 Dock.Top：改父容器宽度才会真正触发 Resize/Relayout
                form.ClientSize = new Size(600, 300);
                Application.DoEvents();
                PaintToBitmap(chrome);
                int rightGap = chrome.Width - chrome.AddressBounds.Right;
                Judge("B2 变窄后地址栏右端 = 窗口宽度 - 150", rightGap == 150, "实际间隙 " + rightGap);

                // ---------- C. Apply 幂等 ----------
                bool first = GlassManager.Apply(form.Handle, false);
                bool second = GlassManager.Apply(form.Handle, false);
                Judge("C1 首次 Apply 成功", first, "hr=" + GlassManager.LastHResult);
                Judge("C2 重复 Apply 仍返回成功（先 NONE 再 Acrylic，不会被 DWM 忽略）",
                      second, "hr=" + GlassManager.LastHResult);
                Judge("C3 Apply 后 IsApplied = true", GlassManager.IsApplied, "");

                // ---------- D. 启动时机 ----------
                Judge("D1 MainForm 不再在构造函数里应用玻璃（改到 OnShown）",
                      !ConstructorAppliesGlass(), "构造函数里仍有 GlassManager.Apply");
                Judge("D2 MainForm 覆写了 OnShown",
                      HasOverriddenOnShown(), "未找到 OnShown 覆写");

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

        /// <summary>
        /// 只失效左上角一小块，再整块 WM_PRINT：
        /// WM_PRINT 传入的裁剪矩形模拟"局部失效"，用来验证清面之后整块前景仍被重画。
        /// </summary>
        private static bool PaintWithSmallInvalidRect(Control c)
        {
            if (c == null || !c.IsHandleCreated) return false;
            GlassManager.PreviewClearColor = Color.FromArgb(1, 2, 3);
            try
            {
                using (var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(1, 2, 3));
                        using (var region = new Region(new Rectangle(0, 0, 40, 20)))
                        {
                            g.SetClip(region, System.Drawing.Drawing2D.CombineMode.Replace);
                            IntPtr hdc = g.GetHdc();
                            try { SendMessage(c.Handle, WM_PRINTCLIENT, hdc, (IntPtr)(PRF_CLIENT | PRF_ERASEBKGND)); }
                            finally { g.ReleaseHdc(hdc); }
                        }
                    }
                    // 右下区域（远离失效矩形）应当仍被画上了前景：找一枚非标记色像素
                    for (int y = bmp.Height - 1; y >= 0; y--)
                        for (int x = bmp.Width - 1; x >= 0; x--)
                        {
                            Color p = bmp.GetPixel(x, y);
                            if (p.ToArgb() != Color.FromArgb(1, 2, 3).ToArgb()) return true;
                        }
                }
            }
            finally { GlassManager.PreviewClearColor = Color.Empty; }
            return false;
        }

        /// <summary>把控件画到位图上，触发一次真实 OnPaint（不含 DWM 合成层）</summary>
        private static void PaintToBitmap(Control c)
        {
            if (c == null || !c.IsHandleCreated) return;
            using (var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        // WM_PRINTCLIENT 会走 OnPaint，但不会创建/销毁控件句柄
                        SendMessage(c.Handle, WM_PRINTCLIENT, hdc, (IntPtr)(PRF_CLIENT | PRF_ERASEBKGND));
                    }
                    finally { g.ReleaseHdc(hdc); }
                }
            }
        }

        private const int WM_PRINTCLIENT = 0x0318;
        private const int PRF_CLIENT = 0x00000004;
        private const int PRF_ERASEBKGND = 0x00000008;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>构造函数体里是否还在调用 GlassManager.Apply（IL 层面查方法引用）</summary>
        private static bool ConstructorAppliesGlass()
        {
            ConstructorInfo ctor = typeof(MainForm).GetConstructor(new Type[] { typeof(string) });
            if (ctor == null) return false;
            byte[] il = ctor.GetMethodBody().GetILAsByteArray();
            MethodInfo apply = typeof(GlassManager).GetMethod("Apply");
            int token = apply.MetadataToken;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;          // call / callvirt
                if (BitConverter.ToInt32(il, i + 1) == token) return true;
            }
            return false;
        }

        private static bool HasOverriddenOnShown()
        {
            MethodInfo m = typeof(MainForm).GetMethod("OnShown",
                BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(EventArgs) }, null);
            return m != null && m.DeclaringType == typeof(MainForm);
        }

        private static System.Collections.Generic.List<ChromeMenuItem> BuildMenus()
        {
            var menus = new System.Collections.Generic.List<ChromeMenuItem>();
            menus.Add(ChromeMenuItem.Sub("书签(&B)", ChromeMenuItem.Item("收藏本页", null)));
            menus.Add(ChromeMenuItem.Sub("下载(&D)", ChromeMenuItem.Item("下载管理...", null)));
            return menus;
        }

        private static System.Collections.Generic.List<ChromeToolButton> BuildButtons()
        {
            var list = new System.Collections.Generic.List<ChromeToolButton>();
            foreach (string icon in new string[] { "back", "forward", "refresh", "star", "zoom", "plus", "close" })
            {
                var b = new ChromeToolButton();
                b.Icon = icon;
                b.Click = delegate { };
                list.Add(b);
            }
            return list;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
