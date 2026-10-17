// ChromePreviewTest.cs —— 浏览器外壳的渲染截图 + 像素级核对
//
// 目的：把 TitleBar + GlassChrome（菜单/工具栏/地址栏/标签头）+ 标签内容区
// 合成成 PNG，让"黑体字形、纯黑文字、文字下方 1px 阴影"是看得见的，
// 而不是只靠读代码推断。
//
// 关于玻璃区（重要，属预期行为）：
//   GlassChrome / TitleBar 的 OnPaintBackground 故意留空，玻璃区靠 DWM 的
//   Acrylic 合成层透出桌面。DrawToBitmap / WM_PRINT 都不含 DWM 合成层，
//   所以直接截图时玻璃区是空白/黑色 —— 这是正常的。本测试因此输出两张图：
//     chrome_preview_raw.png  直接用 DrawToBitmap 截玻璃窗口（玻璃区未绘制 → 一片白，仅作对照）
//     chrome_preview.png      先铺"模拟桌面"底色再逐控件叠加，用于核对文字字形/颜色/阴影
//   玻璃区为空白属正常：DrawToBitmap 不含 DWM 合成层，本图仅用于核对文字字形、颜色与阴影。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class ChromePreviewTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        // 合成图尺寸（Lead 验证参数：窗口 1100x260，外壳 132px + 内容区 128px）
        private const int FormW = 1100;
        private const int FormH = 260;
        private const int ChromeTotal = 132;   // TitleBar 30 + 菜单 26 + 工具栏 36 + 标签头 40

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string log = Path.Combine(baseDir, "chrome_preview_result.txt");
            string shot = Path.Combine(baseDir, "chrome_preview.png");
            string rawShot = Path.Combine(baseDir, "chrome_preview_raw.png");
            string zoomShot = Path.Combine(baseDir, "chrome_preview_zoom.png");
            string labShot = Path.Combine(baseDir, "chrome_preview_text_lab.png");

            Form form = null;
            try
            {
                _sb.AppendLine("=== 浏览器外壳渲染截图（Vista 玻璃 / 黑体 / 纯黑文字 / 1px 阴影）===");
                _sb.AppendLine();
                Info("玻璃区为空白属正常：DrawToBitmap 不含 DWM 合成层，本图仅用于核对文字字形、颜色与阴影。");
                Info("本测试不调用 GlassManager.Apply：测试环境无 DWM 合成层，也不该改动真实窗口。");
                _sb.AppendLine();

                // ---------- A. 字体与配色 ----------
                _sb.AppendLine("-- A. 字体与配色（外壳文字口径） --");
                Font ui = VistaTheme.UIFont;
                Font title = VistaTheme.TitleFont;
                Judge("A1 常规外壳字体族 = SimHei（黑体）",
                      IsSimHei(ui.FontFamily),
                      "实际 FontFamily=" + ui.FontFamily.Name + " / Font.Name=" + ui.Name
                      + " / 英文族名=" + EnglishFamilyName(ui.FontFamily));
                Judge("A1b 标题字体族 = SimHei（黑体）",
                      IsSimHei(title.FontFamily),
                      "实际 FontFamily=" + title.FontFamily.Name
                      + " / 英文族名=" + EnglishFamilyName(title.FontFamily));
                Info("中文系统上 FontFamily.Name 是本地化名「黑体」，其英文族名才是 SimHei —— 两者同一个字体。");
                Judge("A2 常规字号 = 10pt", Math.Abs(ui.Size - 10f) < 0.01f, "实际 " + ui.Size + "pt");
                Judge("A3 标题字号 = 10.5pt", Math.Abs(title.Size - 10.5f) < 0.01f, "实际 " + title.Size + "pt");
                Judge("A4 主文字色 = 纯黑 Color.Black",
                      VistaTheme.Text.ToArgb() == Color.Black.ToArgb(), "实际 " + VistaTheme.Text);
                Judge("A5 阴影偏移常量 = 1px", VistaTheme.ShadowOffset == 1, "实际 " + VistaTheme.ShadowOffset);
                Judge("A6 菜单文本去 & 后为 '书签(B)'（NoPrefix 语义）",
                      VistaTheme.StripAmpersands("书签(&B)") == "书签(B)",
                      "实际 " + VistaTheme.StripAmpersands("书签(&B)"));

                // ---------- B. 搭建外壳 ----------
                form = new Form();
                form.Text = "LoongBrowser";
                form.FormBorderStyle = FormBorderStyle.None;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ClientSize = new Size(FormW, FormH);
                form.BackColor = Color.White;

                var titleBar = new TitleBar(form);
                titleBar.TitleText = "LoongBrowser — 百度一下，你就知道";

                var chrome = new GlassChrome();

                var tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;
                tabs.Visible = true;
                TabStrip.HideHeader(tabs);   // 标签头由 GlassChrome 自绘，TabControl 只当容器

                string[] titles = new string[]
                {
                    "百度一下，你就知道",
                    "Example Domain",
                    "特别特别长的标签页标题用来验证超出宽度时的省略号处理",
                    ""                                   // 空标题 → 回退成"新标签页"
                };
                for (int i = 0; i < titles.Length; i++)
                {
                    var p = new TabPage(titles[i]);
                    p.BackColor = Color.White;
                    tabs.TabPages.Add(p);
                }

                // 加入顺序与 MainForm 一致：Fill 先加，Dock.Top 后加
                form.Controls.Add(tabs);
                form.Controls.Add(chrome);
                form.Controls.Add(titleBar);
                form.Show();
                Application.DoEvents();

                List<ChromeMenuItem> menus = BuildMenus();
                // 外壳内容依赖控件已进容器且宽度已确定（否则 Relayout 宽度为 0）
                chrome.AttachTabs(tabs);
                chrome.SetMenus(menus);
                chrome.SetButtons(BuildButtons());
                chrome.AddressText = "https://www.baidu.com/s?wd=LoongBrowser";
                chrome.ApplyVisibility();
                Application.DoEvents();

                _sb.AppendLine();
                _sb.AppendLine("-- B. 外壳装配与布局 --");
                Judge("B1 标题栏高度 = " + VistaTheme.TitleBarHeight + "px",
                      titleBar.Height == VistaTheme.TitleBarHeight, "实际 " + titleBar.Height);
                int expectedChrome = VistaTheme.MenuBarHeight + VistaTheme.ToolBarHeight + TabStrip.TabHeight;
                Judge("B2 外壳高度 = 菜单" + VistaTheme.MenuBarHeight + " + 工具栏" + VistaTheme.ToolBarHeight +
                      " + 标签头" + TabStrip.TabHeight + " = " + expectedChrome + "px",
                      chrome.Height == expectedChrome, "实际 " + chrome.Height);
                Judge("B3 标题栏 + 外壳 = " + ChromeTotal + "px",
                      titleBar.Height + chrome.Height == ChromeTotal,
                      "实际 " + (titleBar.Height + chrome.Height));
                Judge("B4 Dock 顺序正确（标题栏在最上、外壳其下）",
                      titleBar.Top == 0 && chrome.Top == titleBar.Height,
                      "titleBar.Top=" + titleBar.Top + " chrome.Top=" + chrome.Top);
                Judge("B5 内容区（TabControl）紧接外壳下方",
                      tabs.Top == ChromeTotal && tabs.Height == FormH - ChromeTotal,
                      "Top=" + tabs.Top + " Height=" + tabs.Height + "（期望 " + ChromeTotal + " / " + (FormH - ChromeTotal) + "）");
                Judge("B6 4 个假标签已挂到 GlassChrome 的数据源", tabs.TabPages.Count == 4,
                      "实际 " + tabs.TabPages.Count);

                // ---------- C. 文字像素级核对 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- C. 文字像素级核对（黑体 / 纯黑 / 1px 阴影） --");
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

                using (var full = RenderText("百度一下，你就知道 LoongBrowser", ui, VistaTheme.Text,
                                             VistaTheme.ShadowPolarity.LightSurface, Color.White, flags))
                {
                    int minLum = MinLuminance(full);
                    int black = CountDark(full, new Rectangle(0, 0, full.Width, full.Height), 0);
                    Judge("C1 主字笔画核心足够黑（最低灰度 <= 24）", minLum <= 24, "实测最低灰度 " + minLum);
                    Info("严格纯黑像素数（RGB=0,0,0）= " + black + "（10pt 细笔画下部分像素是抗锯齿灰阶）");
                }

                using (var textOnly = RenderTextOnly("百度一下", ui, VistaTheme.Text, Color.White, flags))
                using (var shadowOnly = RenderText("百度一下", ui, Color.Transparent,
                                             VistaTheme.ShadowPolarity.LightSurface, Color.White, flags))
                {
                    int shadowPixels, visible;
                    double k1, k2, k3;
                    ShadowOffsetStats(textOnly, shadowOnly, Color.White,
                                      out shadowPixels, out visible, out k1, out k2, out k3);
                    Judge("C2 阴影确实被绘制（主字用透明色时仍有非底色像素）", shadowPixels > 0,
                          "阴影像素数 = " + shadowPixels);
                    Info("阴影墨迹 " + shadowPixels + " px，其中 " + visible +
                         " px 露在笔画之外（其余被主字覆盖 —— 阴影本就该藏在笔画后面）");
                    Info("露出的阴影与其上方主字墨迹的贴合率：上方 1 行 → " + Pct(k1) +
                         "，2 行 → " + Pct(k2) + "，3 行 → " + Pct(k3));
                    Judge("C3 阴影紧贴笔画下方 1 行（1 行贴合率 >= 95%）", k1 >= 0.95,
                          "1 行贴合率 " + Pct(k1));
                    Judge("C4 偏移量是 1px 而非 2px（2 行贴合率明显更低）", k2 < k1 - 0.15,
                          "1 行 " + Pct(k1) + " vs 2 行 " + Pct(k2));
                }

                Color glassBack = Color.FromArgb(150, 165, 185);
                using (var gs = RenderText("百度一下", ui, Color.Transparent,
                                           VistaTheme.ShadowPolarity.Glass, glassBack, flags))
                {
                    int lighter = 0, total = 0;
                    int backLum = Lum(glassBack);
                    for (int y = 0; y < gs.Height; y++)
                        for (int x = 0; x < gs.Width; x++)
                        {
                            Color c = gs.GetPixel(x, y);
                            if (!Differs(c, glassBack, 4)) continue;
                            total++;
                            if (Lum(c) > backLum) lighter++;
                        }
                    Judge("C5 Glass 极性阴影是半透明白（玻璃背景上表现为提亮）",
                          total > 0 && lighter == total,
                          "阴影像素 " + total + "，其中提亮 " + lighter);
                }

                // ---------- D. 渲染截图 ----------
                _sb.AppendLine();
                _sb.AppendLine("-- D. 渲染截图 --");

                // D-1 直接用 DrawToBitmap：玻璃区为黑/空白（不含 DWM 合成层）
                bool rawOk = false;
                try
                {
                    using (var raw = new Bitmap(FormW, FormH))
                    {
                        form.DrawToBitmap(raw, new Rectangle(0, 0, FormW, FormH));
                        raw.Save(rawShot, ImageFormat.Png);
                    }
                    rawOk = File.Exists(rawShot);
                    if (rawOk) CopyBesideTest(rawShot, "chrome_preview_raw.png", baseDir);
                }
                catch (Exception ex) { Info("DrawToBitmap 失败：" + ex.Message); }
                Judge("D1 已输出 DrawToBitmap 原始截图（玻璃区一片白，仅作对照）", rawOk, rawShot);
                if (rawOk)
                {
                    Info("raw 截图（tests 下）：" + Path.Combine(Path.GetDirectoryName(baseDir), "chrome_preview_raw.png"));
                    // 量化"玻璃区没画东西"：该处像素是纯白（DWM 合成层不参与 DrawToBitmap）
                    using (var probe = new Bitmap(rawShot))
                    {
                        Color p1 = probe.GetPixel(FormW - 50, 10);
                        Info("raw 图玻璃区像素 (" + (FormW - 50) + ",10) = " + p1 +
                             "（白色＝该处没有任何控件绘制，真实运行时由 Acrylic 透出桌面）");
                    }
                }

                // D-2 可读版合成：先铺"模拟桌面/亚克力"底色，再逐控件 WM_PRINT 叠加。
                //     未绘制的玻璃区保留底色，因此黑字与 1px 阴影都能看清。
                bool shotOk = false, zoomOk = false, labOk = false;
                using (var canvas = MakeBackdrop(FormW, FormH))
                {
                    using (var reference = new Bitmap(canvas))
                    {
                        using (var g = Graphics.FromImage(canvas))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            PrintTo(g, titleBar, 0, 0);
                            PrintTo(g, chrome, 0, titleBar.Height);
                            PrintTo(g, tabs, 0, ChromeTotal);
                        }

                        canvas.Save(shot, ImageFormat.Png);
                        shotOk = File.Exists(shot);

                        // 玻璃区没有铺不透明底色 → 该点的像素与"只有模拟桌面"的参考图一致
                        Color glassPixel = canvas.GetPixel(FormW - 10, titleBar.Height + 8);
                        Color refPixel = reference.GetPixel(FormW - 10, titleBar.Height + 8);
                        Judge("D2 玻璃区不铺底色（像素与模拟桌面底色一致 → 真实运行由 Acrylic 透出）",
                              glassPixel.ToArgb() == refPixel.ToArgb(),
                              "合成图 " + glassPixel + " vs 参考图 " + refPixel);

                        // 文字真的画进图里了：数一下各条带的暗像素
                        int menuDark = CountDark(canvas, new Rectangle(0, titleBar.Height, 800, VistaTheme.MenuBarHeight), 90);
                        int tabDark = CountDark(canvas, new Rectangle(0, titleBar.Height + VistaTheme.MenuBarHeight + VistaTheme.ToolBarHeight,
                                                                     FormW, TabStrip.TabHeight), 90);
                        int titleDark = CountDark(canvas, new Rectangle(0, 0, FormW - VistaTheme.CaptionButtonWidth * 3, titleBar.Height), 90);
                        bool menusLaidOut = true;
                        for (int i = 0; i < menus.Count; i++)
                            if (menus[i].LayoutRect.Width <= 0) menusLaidOut = false;
                        Judge("D3 菜单栏已绘制出文字（暗像素 > 300）", menuDark > 300, "暗像素数 = " + menuDark);
                        Judge("D4 标签头已绘制出文字（暗像素 > 300）", tabDark > 300, "暗像素数 = " + tabDark);
                        Judge("D5 标题栏已绘制出标题文字（暗像素 > 100）", titleDark > 100, "暗像素数 = " + titleDark);
                        Judge("D6 5 个顶层菜单的命中矩形都已回填（菜单绘制路径真的执行了）",
                              menusLaidOut && menus.Count == 5, "菜单数 = " + menus.Count);

                        // 放大图：让 Lead 看清字形与 1px 阴影
                        SaveZoom(canvas, new Rectangle(0, 0, FormW, ChromeTotal + 8), 2, zoomShot);
                        zoomOk = File.Exists(zoomShot);
                    }
                }
                Judge("D7 已输出外壳合成截图 chrome_preview.png", shotOk, shot);
                Judge("D8 已输出 2 倍放大图 chrome_preview_zoom.png（核对字形与阴影）", zoomOk, zoomShot);

                // D-3 文字实验室：白底 / 模拟玻璃底 / 只画阴影，2 倍放大
                using (var lab = BuildTextLab())
                {
                    SaveZoom(lab, new Rectangle(0, 0, lab.Width, lab.Height), 2, labShot);
                    labOk = File.Exists(labShot);
                }
                Judge("D9 已输出文本实验室 chrome_preview_text_lab.png（阴影位置肉眼可核对）", labOk, labShot);

                // 全部把产物拷到 tests 下（脚本也会拷一次，这里保证直接跑 exe 时也有产出）
                if (shotOk) CopyBesideTest(shot, "chrome_preview.png", baseDir);
                if (zoomOk) CopyBesideTest(zoomShot, "chrome_preview_zoom.png", baseDir);
                if (labOk) CopyBesideTest(labShot, "chrome_preview_text_lab.png", baseDir);
                Info("截图（测试运行目录）：" + Path.GetDirectoryName(baseDir));

                // D-4 语义说明
                Info("菜单文本（去 & 后）：" + JoinMenuTexts(menus));
                Info("地址栏文字：" + chrome.AddressText + "（可直接在合成图工具栏里看到）");
                Info("TabControl 标签头高度被压到 " + tabs.ItemSize.Height + "px（TabStrip.HideHeader）");
                _sb.AppendLine();
                Info("玻璃区为空白属正常：DrawToBitmap 不含 DWM 合成层，本图仅用于核对文字字形、颜色与阴影。");
                Info("chrome_preview_raw.png 是纯 DrawToBitmap 产物：玻璃区一片白（未绘制）、前景元素照常可见，属于预期；");
                Info("chrome_preview.png 在其上垫了模拟桌面底色，只用于核对前景文字。");

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
            try { File.WriteAllText(log, _sb.ToString(), Encoding.UTF8); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        // ---------- 外壳内容 ----------

        private static List<ChromeMenuItem> BuildMenus()
        {
            var menus = new List<ChromeMenuItem>();
            menus.Add(ChromeMenuItem.Sub("书签(&B)",
                ChromeMenuItem.Item("收藏本页", null),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("书签管理...", null)));
            menus.Add(ChromeMenuItem.Sub("下载(&D)",
                ChromeMenuItem.Item("下载管理...", null)));
            menus.Add(ChromeMenuItem.Sub("历史(&H)",
                ChromeMenuItem.Item("查看历史记录...", null),
                ChromeMenuItem.Item("清空全部历史记录", null)));
            menus.Add(ChromeMenuItem.Sub("查看(&V)",
                ChromeMenuItem.Item("放大", null),
                ChromeMenuItem.Item("缩小", null),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("实际大小（100%）", null)));
            menus.Add(ChromeMenuItem.Sub("工具(&T)",
                ChromeMenuItem.Item("清理缓存", null),
                ChromeMenuItem.Item("翻译当前页面", null),
                ChromeMenuItem.Separator(),
                ChromeMenuItem.Item("关于 LoongBrowser", null)));
            return menus;
        }

        private static List<ChromeToolButton> BuildButtons()
        {
            var list = new List<ChromeToolButton>();
            list.Add(Button("back", "后退"));
            list.Add(Button("forward", "前进"));
            list.Add(Button("refresh", "刷新"));
            list.Add(Sep());
            list.Add(Button("star", "收藏本页"));
            list.Add(Button("translate", "翻译"));
            list.Add(Sep());
            list.Add(Button("zoom", "网页缩放"));
            list.Add(Button("plus", "新标签"));
            list.Add(Button("close", "关闭当前标签"));
            return list;
        }

        private static ChromeToolButton Button(string icon, string tip)
        {
            var b = new ChromeToolButton();
            b.Icon = icon;
            b.Tooltip = tip;
            b.Click = delegate { };
            return b;
        }

        private static ChromeToolButton Sep()
        {
            var b = new ChromeToolButton();
            b.Separator = true;
            return b;
        }

        private static string JoinMenuTexts(List<ChromeMenuItem> menus)
        {
            var parts = new List<string>();
            for (int i = 0; i < menus.Count; i++) parts.Add(VistaTheme.StripAmpersands(menus[i].Text));
            return string.Join("  ", parts.ToArray());
        }

        // ---------- 合成与像素工具 ----------

        /// <summary>模拟桌面/亚克力底色：真实运行时这一层由 DWM 合成，测试里用浅蓝渐变代替。</summary>
        private static Bitmap MakeBackdrop(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                var full = new Rectangle(0, 0, w, h);
                using (var lg = new LinearGradientBrush(full, Color.FromArgb(226, 236, 247), Color.FromArgb(178, 196, 216), 90f))
                    g.FillRectangle(lg, full);
                // 内容区（真实运行时是网页）铺白
                g.FillRectangle(Brushes.White, new Rectangle(0, ChromeTotal, w, h - ChromeTotal));
            }
            return bmp;
        }

        private const int WM_PRINT = 0x0317;
        private const int PRF_NONCLIENT = 0x0002;
        private const int PRF_CLIENT = 0x0004;
        private const int PRF_ERASEBKGND = 0x0008;
        private const int PRF_CHILDREN = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("gdi32.dll")]
        private static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, out POINT previous);

        /// <summary>
        /// 把控件"画"到已有位图上的 (ox, oy) 处。
        /// 用 WM_PRINT 而不是 DrawToBitmap：后者内部会新建一张临时位图再整体拷贝，
        /// 未绘制的玻璃区会以黑/空白覆盖掉底下的模拟桌面，文字就看不见了。
        /// WM_PRINT 只画控件真正画出的像素，其余保持底色 —— 这正是真实玻璃的语义。
        /// </summary>
        private static void PrintTo(Graphics g, Control c, int ox, int oy)
        {
            if (c == null || !c.IsHandleCreated) return;
            IntPtr hdc = g.GetHdc();
            try
            {
                POINT prev;
                SetViewportOrgEx(hdc, ox, oy, out prev);
                int flags = PRF_CLIENT | PRF_CHILDREN | PRF_ERASEBKGND | PRF_NONCLIENT;
                SendMessage(c.Handle, WM_PRINT, hdc, (IntPtr)flags);
                POINT dummy;
                SetViewportOrgEx(hdc, prev.X, prev.Y, out dummy);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        /// <summary>把运行目录里的截图拷到 tests\ 下（脚本也会拷，这里保证单独跑 exe 时也有产物）。</summary>
        private static void CopyBesideTest(string from, string name, string baseDir)
        {
            try
            {
                string to = Path.Combine(Path.GetDirectoryName(baseDir), name);
                File.Copy(from, to, true);
            }
            catch (Exception) { }
        }

        private static void SaveZoom(Bitmap src, Rectangle crop, int scale, string path)
        {
            using (var bmp = new Bitmap(crop.Width * scale, crop.Height * scale))
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), crop, GraphicsUnit.Pixel);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>文本实验室：白底 / 模拟玻璃底 / 只画阴影 三种情况并排，2 倍放大后肉眼可核对阴影位置。</summary>
        private static Bitmap BuildTextLab()
        {
            int w = 880, h = 400;
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                Font ui = VistaTheme.UIFont;

                TextRenderer.DrawText(g, "1) 白底卡片 + LightSurface：主字纯黑，每笔画下方 1px 半透明黑影",
                    ui, new Rectangle(12, 4, w - 24, 20), Color.FromArgb(105, 118, 132));
                VistaTheme.DrawChromeText(g, "百度一下，你就知道  LoongBrowser 黑体 SimHei 10pt", ui,
                    new Rectangle(12, 26, w - 24, 26), VistaTheme.Text, VistaTheme.ShadowPolarity.LightSurface, f);

                var glass = new Rectangle(0, 62, w, 54);
                using (var lg = new LinearGradientBrush(glass, Color.FromArgb(226, 236, 247), Color.FromArgb(178, 196, 216), 0f))
                    g.FillRectangle(lg, glass);
                TextRenderer.DrawText(g, "2) 模拟玻璃底 + Glass：主字纯黑，每笔画下方 1px 半透明白光",
                    ui, new Rectangle(12, 66, w - 24, 20), Color.FromArgb(55, 68, 84));
                VistaTheme.DrawChromeText(g, "百度一下，你就知道  LoongBrowser 黑体 SimHei 10pt", ui,
                    new Rectangle(12, 90, w - 24, 26), VistaTheme.Text, VistaTheme.ShadowPolarity.Glass, f);

                TextRenderer.DrawText(g, "3) 下面是「百度」二字的 8 倍像素放大：左＝只画阴影（主字透明）· 右＝主字含阴影",
                    ui, new Rectangle(12, 122, w - 24, 20), Color.FromArgb(105, 118, 132));

                using (var shadowOnly = RenderText("百度", ui, Color.Transparent,
                                                   VistaTheme.ShadowPolarity.LightSurface, Color.White, f))
                using (var both = RenderText("百度", ui, VistaTheme.Text,
                                             VistaTheme.ShadowPolarity.LightSurface, Color.White, f))
                {
                    var crop = InkBounds(shadowOnly, Color.White);
                    crop.Inflate(2, 2);
                    int scale = 8;
                    var dstL = new Rectangle(30, 142, crop.Width * scale, crop.Height * scale);
                    var dstR = new Rectangle(dstL.Right + 40, 142, crop.Width * scale, crop.Height * scale);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(shadowOnly, dstL, crop, GraphicsUnit.Pixel);
                    g.DrawImage(both, dstR, crop, GraphicsUnit.Pixel);
                    g.DrawRectangle(Pens.Gray, dstL);
                    g.DrawRectangle(Pens.Gray, dstR);
                    TextRenderer.DrawText(g, "左＝阴影（每条笔画正下方多出 1 行浅灰像素）· 右＝主字（阴影藏在笔画后，只在笔画下沿露出 1 行）",
                        ui, new Rectangle(12, dstR.Bottom + 6, w - 24, 20), Color.FromArgb(70, 82, 96));
                    TextRenderer.DrawText(g, "（两张图裁自同一块区域、同一缩放比；左图就是 DrawChromeText 里 shadowRect 那一遍的输出）",
                        ui, new Rectangle(12, dstR.Bottom + 28, w - 24, 20), Color.FromArgb(120, 132, 146));
                }
            }
            return bmp;
        }

        /// <summary>取图中墨迹（非底色）的包围盒，用于放大时紧贴文字。</summary>
        private static Rectangle InkBounds(Bitmap bmp, Color back)
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (!Differs(bmp.GetPixel(x, y), back, 4)) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return new Rectangle(0, 0, 1, 1);
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>只画主字（不带阴影），口径与 DrawChromeText 的主字那一遍完全一致，用于反推阴影位移。</summary>
        private static Bitmap RenderTextOnly(string text, Font font, Color color, Color back, TextFormatFlags flags)
        {
            var bmp = new Bitmap(600, 90);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var fmt = new StringFormat(StringFormat.GenericTypographic))
                {
                    fmt.FormatFlags |= StringFormatFlags.NoWrap;
                    fmt.Alignment = StringAlignment.Near;
                    fmt.LineAlignment = StringAlignment.Center;
                    fmt.Trimming = StringTrimming.None;
                    using (var b = new SolidBrush(color))
                        g.DrawString(text, font, b, new Rectangle(0, 0, bmp.Width, bmp.Height), fmt);
                }
            }
            return bmp;
        }

        private static Bitmap RenderText(string text, Font font, Color color,
                                         VistaTheme.ShadowPolarity polarity, Color back, TextFormatFlags flags)
        {
            var bmp = new Bitmap(600, 90);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                VistaTheme.DrawChromeText(g, text, font, new Rectangle(0, 0, bmp.Width, bmp.Height),
                                          color, polarity, flags);
            }
            return bmp;
        }

        private static int Lum(Color c)
        {
            return (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        }

        private static bool Differs(Color a, Color b, int tol)
        {
            return Math.Abs(a.R - b.R) > tol || Math.Abs(a.G - b.G) > tol || Math.Abs(a.B - b.B) > tol;
        }

        private static int MinLuminance(Bitmap bmp)
        {
            int min = 255;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    int l = Lum(bmp.GetPixel(x, y));
                    if (l < min) min = l;
                }
            return min;
        }

        private static int CountDark(Bitmap bmp, Rectangle r, int maxLum)
        {
            int n = 0;
            int x0 = Math.Max(0, r.Left), x1 = Math.Min(bmp.Width, r.Right);
            int y0 = Math.Max(0, r.Top), y1 = Math.Min(bmp.Height, r.Bottom);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (Lum(bmp.GetPixel(x, y)) <= maxLum) n++;
            return n;
        }

        /// <summary>
        /// 量化阴影位移：阴影位图（主字用透明色画，只剩阴影）里
        /// "露在笔画之外"的那部分像素（主字单独渲染的 textOnly 在同一处没有墨迹），
        /// 看它正上方 dy 行处主字是否有墨迹。
        /// 阴影 = 主字整体下移 1px，所以这层露出的 1px 应当紧贴笔画下沿（dy=1 贴合率≈100%），
        /// 而 dy=2 只是笔画本身的宽度延伸，贴合率会明显更低 —— 两者之差就是"偏移只有 1px"的证据。
        /// </summary>
        private static void ShadowOffsetStats(Bitmap textOnly, Bitmap shadow, Color back,
                                              out int shadowPixels, out int visible,
                                              out double k1, out double k2, out double k3)
        {
            shadowPixels = 0;
            visible = 0;
            int m1 = 0, m2 = 0, m3 = 0;
            for (int y = 0; y < shadow.Height; y++)
                for (int x = 0; x < shadow.Width; x++)
                {
                    if (!Differs(shadow.GetPixel(x, y), back, 4)) continue;
                    shadowPixels++;
                    // 只统计"阴影可见"的位置：主字在同一处没有墨迹，露出的就是那条 1px 阴影
                    if (Differs(textOnly.GetPixel(x, y), back, 4)) continue;
                    visible++;
                    if (y >= 1 && Differs(textOnly.GetPixel(x, y - 1), back, 4)) m1++;
                    if (y >= 2 && Differs(textOnly.GetPixel(x, y - 2), back, 4)) m2++;
                    if (y >= 3 && Differs(textOnly.GetPixel(x, y - 3), back, 4)) m3++;
                }
            k1 = visible == 0 ? 0 : (double)m1 / visible;
            k2 = visible == 0 ? 0 : (double)m2 / visible;
            k3 = visible == 0 ? 0 : (double)m3 / visible;
        }

        /// <summary>判断是不是黑体：中文系统上 FontFamily.Name 是本地化名「黑体」，需要用英文族名比对。</summary>
        private static bool IsSimHei(FontFamily family)
        {
            if (family == null) return false;
            if (string.Equals(family.Name, "SimHei", StringComparison.OrdinalIgnoreCase)) return true;
            if (family.Name == "黑体") return true;
            return string.Equals(EnglishFamilyName(family), "SimHei", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>取字体族的英文名（中文系统上 Name 是「黑体」，en-US 族名才是 SimHei）。</summary>
        private static string EnglishFamilyName(FontFamily family)
        {
            if (family == null) return "(null)";
            try { return family.GetName(0x0409); } catch (Exception) { return family.Name; }
        }

        private static string Pct(double v)
        {
            return (v * 100.0).ToString("0.0") + "%";
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
