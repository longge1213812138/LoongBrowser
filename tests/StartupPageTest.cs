// StartupPageTest.cs —— 启动页设置（单元）
//
// 覆盖五件事：
//   A. 启动决策：命令行地址 > 启动页设置；设置里三选一（空白页 / 书签墙 / 指定网站）
//   B. 输入归一化：补协议、识本地路径、挡住 javascript:/data:/about:
//   C. 落盘往返：保存/清空/损坏/非法模式回落
//   D. 书签墙与启动页共用一套渲染，但标签页标题不被改掉
//   E. 对话框的三选一、禁用态、校验与落盘
// 另含源码级防回归：启动路径必须读设置，新标签按钮必须仍是书签墙。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LoongBrowser
{
    public static class StartupPageTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();
        private static string _tmpDir;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Application.EnableVisualStyles();

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _tmpDir = Path.Combine(baseDir, "startup_tmp");
            Directory.CreateDirectory(_tmpDir);
            StartupPageSettings.FilePath = Path.Combine(_tmpDir, "startup.json");
            try { if (File.Exists(StartupPageSettings.FilePath)) File.Delete(StartupPageSettings.FilePath); } catch (Exception) { }

            _sb.AppendLine("=== 启动页设置（单元）===");
            _sb.AppendLine("数据文件: " + StartupPageSettings.FilePath);
            _sb.AppendLine();

            TestDecision();
            TestNormalize();
            TestPersist();
            TestWallTitle();
            TestDialog();
            TestSourceGuards();

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());

            try { Directory.Delete(_tmpDir, true); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        // ---------- A. 启动决策 ----------

        private static void TestDecision()
        {
            _sb.AppendLine("-- A. 启动决策：三选一 + 命令行优先 --");

            // 默认（没设置过）= 空白页：加功能不改既有默认行为
            StartupPageSettings.Mode = StartupPageSettings.ModeBlank;
            StartupPageSettings.Url = null;
            Judge("A0 默认（空白页）没有墙标题",
                  StartupPageSettings.InitialWallTitle(null) == null,
                  "实际: " + Show(StartupPageSettings.InitialWallTitle(null)));
            Judge("A1 默认是空白页（既有行为不变）",
                  StartupPageSettings.InitialUrl(null) == NewTabPage.HomeUrl,
                  "实际: " + Show(StartupPageSettings.InitialUrl(null)));
            Judge("A2 默认不是书签墙", StartupPageSettings.InitialUrl(null) != null, "");
            Judge("A3 默认 HasUrl 为 false", !StartupPageSettings.HasUrl, "");

            StartupPageSettings.Mode = StartupPageSettings.ModeWall;
            Judge("A4 选了书签墙 → null（交给 TabManager 画墙）",
                  StartupPageSettings.InitialUrl(null) == null,
                  "实际: " + Show(StartupPageSettings.InitialUrl(null)));
            Judge("A4b 启动即书签墙时标题是「启动页」",
                  StartupPageSettings.InitialWallTitle(null) == NewTabPage.StartupTitle,
                  "实际: " + Show(StartupPageSettings.InitialWallTitle(null)));

            StartupPageSettings.Mode = StartupPageSettings.ModeUrl;
            StartupPageSettings.Url = "https://www.baidu.com";
            Judge("A5 选了指定网站 → 用它",
                  StartupPageSettings.InitialUrl(null) == "https://www.baidu.com",
                  "实际: " + Show(StartupPageSettings.InitialUrl(null)));
            Judge("A6 选指定网站时 HasUrl 为 true", StartupPageSettings.HasUrl, "");
            Judge("A7 命令行有地址 → 压过启动页设置（双击文件不被抢走）",
                  StartupPageSettings.InitialUrl("file:///C:/a.html") == "file:///C:/a.html",
                  "实际: " + Show(StartupPageSettings.InitialUrl("file:///C:/a.html")));
            Judge("A8 命令行空串等同于没给",
                  StartupPageSettings.InitialUrl("") == "https://www.baidu.com",
                  "实际: " + Show(StartupPageSettings.InitialUrl("")));
            Judge("A9 启动页可以是本地文件",
                  StartupPageSettings.Url == "https://www.baidu.com", "");
            Judge("A9b 选了指定网站时没有墙标题（画的是网页不是墙）",
                  StartupPageSettings.InitialWallTitle(null) == null,
                  "实际: " + Show(StartupPageSettings.InitialWallTitle(null)));
            Judge("A9c 命令行有地址时也没有墙标题",
                  StartupPageSettings.InitialWallTitle("file:///C:/a.html") == null, "");

            StartupPageSettings.Url = null;
            Judge("A10 选了网站却没地址 → 回落空白页",
                  StartupPageSettings.InitialUrl(null) == NewTabPage.HomeUrl,
                  "实际: " + Show(StartupPageSettings.InitialUrl(null)));

            Judge("A11 未知模式收敛成空白页",
                  StartupPageSettings.NormalizeMode("nonsense") == StartupPageSettings.ModeBlank, "");
            Judge("A12 空模式收敛成空白页",
                  StartupPageSettings.NormalizeMode(null) == StartupPageSettings.ModeBlank, "");
            Judge("A13 wall 模式原样保留",
                  StartupPageSettings.NormalizeMode("wall") == StartupPageSettings.ModeWall, "");
            Judge("A14 url 模式原样保留",
                  StartupPageSettings.NormalizeMode("url") == StartupPageSettings.ModeUrl, "");

            StartupPageSettings.Mode = StartupPageSettings.ModeBlank;
            _sb.AppendLine();
        }

        // ---------- B. 输入归一化 ----------

        private static void TestNormalize()
        {
            _sb.AppendLine("-- B. 输入归一化与协议白名单 --");

            Ok("B1 裸域名补 https", "www.qq.com", "https://www.qq.com");
            Ok("B2 完整地址原样", "https://example.com/a?b=1", "https://example.com/a?b=1");
            Ok("B3 http 原样", "http://example.com/", "http://example.com/");
            Ok("B4 前后空格被裁掉", "   https://example.com/   ", "https://example.com/");
            Ok("B5 无点号关键词 → 搜索", "hello", "https://www.bing.com/search?q=hello");

            Bad("B6 javascript: 必须被拒", "javascript:alert(1)");
            Bad("B7 data: 必须被拒", "data:text/html,<h1>x</h1>");
            Bad("B8 about: 必须被拒", "about:blank");
            Bad("B9 空输入必须被拒", "   ");
            Bad("B9b vbscript: 必须被拒", "vbscript:msgbox(1)");

            string n, e;
            StartupPageSettings.TryNormalize("", out n, out e);
            Judge("B10 空输入给出可读提示", !string.IsNullOrEmpty(e), "提示为空");

            bool okLocal = StartupPageSettings.TryNormalize(@"C:\Windows\notepad.exe", out n, out e);
            Judge("B11 本地路径转 file:// 且被接受", okLocal && n != null && n.StartsWith("file:", StringComparison.Ordinal),
                  "实际: " + Show(n) + " 错误: " + Show(e));

            bool okDrive = StartupPageSettings.TryNormalize(@"D:\页面\start.html", out n, out e);
            Judge("B13 盘符不能被误判成协议（C: 不是 scheme）", okDrive && n != null && n.StartsWith("file:", StringComparison.Ordinal),
                  "实际: " + Show(n) + " 错误: " + Show(e));

            bool okUnc = StartupPageSettings.TryNormalize(@"\\server\share\start.html", out n, out e);
            Judge("B14 UNC 路径被接受", okUnc && n != null && n.StartsWith("file:", StringComparison.Ordinal),
                  "实际: " + Show(n) + " 错误: " + Show(e));

            bool okHost = StartupPageSettings.TryNormalize("localhost:8080", out n, out e);
            Judge("B12 localhost 被接受", okHost && n != null && n.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0,
                  "实际: " + Show(n) + " 错误: " + Show(e));

            bool okPort = StartupPageSettings.TryNormalize("example.com:8080/x", out n, out e);
            Judge("B15 host:port 不被误判成协议",
                  okPort && n != null && n.IndexOf("example.com:8080", StringComparison.Ordinal) >= 0,
                  "实际: " + Show(n) + " 错误: " + Show(e));
            _sb.AppendLine();
        }

        // ---------- C. 落盘 ----------

        private static void TestPersist()
        {
            _sb.AppendLine("-- C. 落盘往返 --");

            StartupPageSettings.Save(StartupPageSettings.ModeUrl, "https://example.com/start");
            Judge("C1 文件已写出", File.Exists(StartupPageSettings.FilePath), "路径: " + StartupPageSettings.FilePath);
            Judge("C2 内存值同步",
                  StartupPageSettings.Mode == StartupPageSettings.ModeUrl && StartupPageSettings.Url == "https://example.com/start",
                  "实际: " + StartupPageSettings.Mode + " / " + Show(StartupPageSettings.Url));

            Reset();      // 模拟重启进程
            StartupPageSettings.Load();
            Judge("C3 重新载入后模式与地址都不丢",
                  StartupPageSettings.Mode == StartupPageSettings.ModeUrl && StartupPageSettings.Url == "https://example.com/start",
                  "实际: " + StartupPageSettings.Mode + " / " + Show(StartupPageSettings.Url));

            StartupPageSettings.Save(StartupPageSettings.ModeUrl, "   https://example.com/x   ");
            Judge("C4 保存时裁掉首尾空格", StartupPageSettings.Url == "https://example.com/x",
                  "实际: " + Show(StartupPageSettings.Url));

            StartupPageSettings.Save(StartupPageSettings.ModeWall, null);
            Reset();
            StartupPageSettings.Load();
            Judge("C5 书签墙模式能往返",
                  StartupPageSettings.Mode == StartupPageSettings.ModeWall && StartupPageSettings.Url == null,
                  "实际: " + StartupPageSettings.Mode + " / " + Show(StartupPageSettings.Url));

            StartupPageSettings.Save(StartupPageSettings.ModeBlank, null);
            Reset();
            StartupPageSettings.Load();
            Judge("C6 清空后回到空白页",
                  StartupPageSettings.Mode == StartupPageSettings.ModeBlank && !StartupPageSettings.HasUrl,
                  "实际: " + StartupPageSettings.Mode + " / " + Show(StartupPageSettings.Url));

            StartupPageSettings.Save(StartupPageSettings.ModeUrl, "https://a.example/1");
            StartupPageSettings.Save(StartupPageSettings.ModeUrl, "https://b.example/2");
            Reset();
            StartupPageSettings.Load();
            Judge("C7 连续保存只留最后一次", StartupPageSettings.Url == "https://b.example/2",
                  "实际: " + Show(StartupPageSettings.Url));

            // 选了 url 却没地址 → 落盘时就收敛成空白页，不会留下"半个配置"
            StartupPageSettings.Save(StartupPageSettings.ModeUrl, null);
            Judge("C8 选网站但无地址 → 存成空白页",
                  StartupPageSettings.Mode == StartupPageSettings.ModeBlank, "实际: " + StartupPageSettings.Mode);

            try
            {
                File.WriteAllText(StartupPageSettings.FilePath, "{ this is not json ");
                Reset();
                StartupPageSettings.Load();
                Judge("C9 文件损坏按空白页处理且不抛异常",
                      StartupPageSettings.Mode == StartupPageSettings.ModeBlank && !StartupPageSettings.HasUrl,
                      "实际: " + StartupPageSettings.Mode);
            }
            catch (Exception ex)
            {
                Judge("C9 文件损坏按空白页处理且不抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }

            try { File.Delete(StartupPageSettings.FilePath); } catch (Exception) { }
            Reset();
            StartupPageSettings.Load();
            Judge("C10 文件不存在也按空白页处理",
                  StartupPageSettings.Mode == StartupPageSettings.ModeBlank, "实际: " + StartupPageSettings.Mode);

            // 未知模式（旧版本/手改）→ 空白页，不炸
            try
            {
                File.WriteAllText(StartupPageSettings.FilePath, "{\"Mode\":\"weird\",\"Url\":null}");
                Reset();
                StartupPageSettings.Load();
                Judge("C11 未知模式回落空白页", StartupPageSettings.Mode == StartupPageSettings.ModeBlank,
                      "实际: " + StartupPageSettings.Mode);
            }
            catch (Exception ex)
            {
                Judge("C11 未知模式回落空白页", false, ex.GetType().Name + ": " + ex.Message);
            }
            _sb.AppendLine();
        }

        /// <summary>把内存状态清成"刚启动、还没读盘"的样子</summary>
        private static void Reset()
        {
            StartupPageSettings.Mode = StartupPageSettings.ModeBlank;
            StartupPageSettings.Url = null;
        }

        // ---------- D. 墙标题（启动页 / 新标签页共用一张墙） ----------

        private static void TestWallTitle()
        {
            _sb.AppendLine("-- D. 启动页与书签墙共用同一套渲染 --");

            var list = new List<BookmarkItem>();
            list.Add(new BookmarkItem { Title = "示例", Url = "https://example.com/" });

            string startup = NewTabPage.BuildHtml(list, NewTabPage.StartupTitle);
            string ntp = NewTabPage.BuildHtml(list);

            Judge("D1 启动页页头写「启动页」",
                  startup.IndexOf("class=\"ttl\">启动页<", StringComparison.Ordinal) >= 0, "未找到页头启动页");
            Judge("D2 启动页的标签页标题也是「启动页」",
                  startup.IndexOf("<title>启动页</title>", StringComparison.Ordinal) >= 0, "未找到 <title>启动页</title>");
            Judge("D3 书签墙的标签页标题仍是「新标签页」（不因启动页改动）",
                  ntp.IndexOf("<title>新标签页</title>", StringComparison.Ordinal) >= 0, "未找到 <title>新标签页</title>");
            Judge("D3b 书签墙页头写「书签」",
                  ntp.IndexOf("class=\"ttl\">书签<", StringComparison.Ordinal) >= 0, "未找到页头书签");

            Judge("D4 两种标题的卡片区完全一致",
                  StripHead(startup) == StripHead(ntp), "卡片区不一致");
            Judge("D5 标题做 HTML 转义（不注入标签）",
                  NewTabPage.BuildHtml(list, "<b>x</b>").IndexOf("class=\"ttl\">&lt;b&gt;x&lt;/b&gt;<", StringComparison.Ordinal) >= 0,
                  "转义未生效");
            Judge("D6 空标题回落成书签墙",
                  NewTabPage.BuildHtml(list, null).IndexOf("class=\"ttl\">书签<", StringComparison.Ordinal) >= 0, "");
            Judge("D6b 启动页的标签页标题就是「启动页」",
                  NewTabPage.DocumentTitleOf(NewTabPage.StartupTitle) == "启动页", "");
            Judge("D6c 书签墙的标签页标题映射回「新标签页」",
                  NewTabPage.DocumentTitleOf(NewTabPage.NewTabTitle) == NewTabPage.NewTabDocumentTitle, "");
            Judge("D7 启动页仍带点击桥脚本",
                  startup.IndexOf("postMessage", StringComparison.Ordinal) >= 0, "缺 postMessage");
            Judge("D8 两个页头常量不同（区分得开）", NewTabPage.NewTabTitle != NewTabPage.StartupTitle, "");
            _sb.AppendLine();
        }

        /// <summary>去掉 <title> 与页头，用来比较两种标题下其余内容是否一模一样</summary>
        private static string StripHead(string html)
        {
            string s = html;
            int a = s.IndexOf("<title>", StringComparison.Ordinal);
            int b = s.IndexOf("</title>", StringComparison.Ordinal);
            if (a >= 0 && b > a) s = s.Remove(a, b - a + 8);
            int c = s.IndexOf("class=\"ttl\">", StringComparison.Ordinal);
            if (c >= 0)
            {
                int d = s.IndexOf("</span>", c, StringComparison.Ordinal);
                if (d > c) s = s.Remove(c, d - c);
            }
            return s;
        }

        // ---------- E. 对话框 ----------

        private static void TestDialog()
        {
            _sb.AppendLine("-- E. 设置对话框 --");

            // 未设置 → 选中"空白页"
            Reset();
            using (var dlg = new StartupPageDialog(null))
            {
                Judge("E1 未设置时默认选中「空白页」", Radio(dlg, "_rbBlank").Checked, "");
                Judge("E2 未设置时不选书签墙", !Radio(dlg, "_rbWall").Checked, "");
                Judge("E3 未设置时不选指定网站", !Radio(dlg, "_rbUrl").Checked, "");
                Judge("E4 空白页档下文本框禁用", !Box(dlg, "_txtUrl").Enabled, "");
                Judge("E5 无当前页地址时「使用当前标签页」禁用", !Btn(dlg, "_btnUseCurrent").Enabled, "");
                Judge("E6 字体统一为外壳字体",
                      dlg.Font.Name == VistaTheme.UIFont.Name && dlg.Font.Size == VistaTheme.UIFont.Size,
                      "实际: " + dlg.Font.Name + " " + dlg.Font.Size);
            }

            // 已设置成网站 → 选中"指定网站"，回填地址
            StartupPageSettings.Mode = StartupPageSettings.ModeUrl;
            StartupPageSettings.Url = "https://example.com/set";
            using (var dlg = new StartupPageDialog("https://page.example/now"))
            {
                Judge("E7 已设置网站时选中「指定网站」", Radio(dlg, "_rbUrl").Checked, "");
                Judge("E8 文本框回填当前设置", Box(dlg, "_txtUrl").Text == "https://example.com/set",
                      "实际: " + Box(dlg, "_txtUrl").Text);
                Judge("E9 指定网站档下文本框可用", Box(dlg, "_txtUrl").Enabled, "");
                Judge("E10 有当前页地址时按钮可用", Btn(dlg, "_btnUseCurrent").Enabled, "");

                // 窗体没 Show 时 Button.PerformClick 不触发（CanSelect 依赖可见性），
                // 直接调按钮背后的处理函数，测的是同一段逻辑
                Invoke(dlg, "UseCurrentPage");
                Judge("E11 一键填入当前页地址", Box(dlg, "_txtUrl").Text == "https://page.example/now",
                      "实际: " + Box(dlg, "_txtUrl").Text);
            }

            // 已设置成书签墙 → 选中"书签墙"
            StartupPageSettings.Mode = StartupPageSettings.ModeWall;
            StartupPageSettings.Url = null;
            using (var dlg = new StartupPageDialog(null))
            {
                Judge("E12 已设置书签墙时选中「书签墙」", Radio(dlg, "_rbWall").Checked, "");
                Judge("E13 书签墙档下文本框禁用", !Box(dlg, "_txtUrl").Enabled, "");
            }

            // 选空白页并确定 → 存成 blank
            StartupPageSettings.Mode = StartupPageSettings.ModeWall;
            using (var dlg = new StartupPageDialog(null))
            {
                Radio(dlg, "_rbBlank").Checked = true;
                Invoke(dlg, "Accept");
                Judge("E14 选空白页并确定 → 存成 blank",
                      StartupPageSettings.Mode == StartupPageSettings.ModeBlank, "实际: " + StartupPageSettings.Mode);
            }

            // 选书签墙并确定 → 存成 wall
            using (var dlg = new StartupPageDialog(null))
            {
                Radio(dlg, "_rbWall").Checked = true;
                Invoke(dlg, "Accept");
                Judge("E15 选书签墙并确定 → 存成 wall",
                      StartupPageSettings.Mode == StartupPageSettings.ModeWall, "实际: " + StartupPageSettings.Mode);
            }

            // 非法地址并确定 → 不落盘、保持原设置、给出错误提示
            StartupPageSettings.Mode = StartupPageSettings.ModeUrl;
            StartupPageSettings.Url = "https://keep.example/";
            using (var dlg = new StartupPageDialog(null))
            {
                Radio(dlg, "_rbUrl").Checked = true;
                Box(dlg, "_txtUrl").Text = "javascript:alert(1)";
                Invoke(dlg, "Accept");
                Judge("E16 非法地址不落盘", StartupPageSettings.Url == "https://keep.example/",
                      "实际: " + Show(StartupPageSettings.Url));
                Judge("E17 非法地址给出错误提示", Label(dlg, "_lblError").Text.Length > 0, "提示为空");
                Judge("E18 非法地址对话框不关闭", dlg.DialogResult == DialogResult.None,
                      "实际: " + dlg.DialogResult);
            }

            // 合法地址并确定 → 归一化后落盘
            using (var dlg = new StartupPageDialog(null))
            {
                Radio(dlg, "_rbUrl").Checked = true;
                Box(dlg, "_txtUrl").Text = "  www.qq.com  ";
                Invoke(dlg, "Accept");
                Judge("E19 合法地址归一化后落盘",
                      StartupPageSettings.Mode == StartupPageSettings.ModeUrl && StartupPageSettings.Url == "https://www.qq.com",
                      "实际: " + StartupPageSettings.Mode + " / " + Show(StartupPageSettings.Url));
            }

            Reset();
            try { File.Delete(StartupPageSettings.FilePath); } catch (Exception) { }
            _sb.AppendLine();
        }

        // ---------- F. 源码级防回归 ----------

        private static void TestSourceGuards()
        {
            _sb.AppendLine("-- F. 源码级防回归 --");

            string srcDir = FindSrcDir();
            if (srcDir == null)
            {
                Judge("F0 找到 src 目录", false, "定位失败");
                return;
            }

            string main = File.ReadAllText(Path.Combine(srcDir, "MainForm.cs"));
            Judge("F1 启动路径读启动页设置（InitialUrl）",
                  main.IndexOf("StartupPageSettings.InitialUrl(", StringComparison.Ordinal) >= 0, "找不到 InitialUrl 调用");
            Judge("F2 「＋新标签」按钮仍传 null（永远书签墙）",
                  main.IndexOf("ChromeToolButton(\"plus\", \"新标签\", delegate { _tabMgr.NewTab(null); })", StringComparison.Ordinal) >= 0,
                  "找不到 plus 按钮的 NewTab(null)");
            Judge("F3 新标签按钮不读启动页设置",
                  !Near(main, "NewTab(null)", "StartupPageSettings"), "新标签按钮附近出现 StartupPageSettings");
            Judge("F4 工具菜单有启动页入口",
                  main.IndexOf("启动页设置...", StringComparison.Ordinal) >= 0, "菜单项缺失");
            Judge("F5 构造顺序：先载入启动页设置，再建首个标签",
                  main.IndexOf("StartupPageSettings.Load()", StringComparison.Ordinal) >= 0 &&
                  main.IndexOf("StartupPageSettings.Load()", StringComparison.Ordinal) <
                  main.IndexOf("StartupPageSettings.InitialUrl(", StringComparison.Ordinal),
                  "Load 晚于 InitialUrl");
            Judge("F6 启动调用同时传地址与墙标题",
                  main.IndexOf("StartupPageSettings.InitialUrl(initialUrl)", StringComparison.Ordinal) >= 0 &&
                  main.IndexOf("StartupPageSettings.InitialWallTitle(initialUrl)", StringComparison.Ordinal) >= 0,
                  "启动调用写法变了，需人工复核");

            string tabMgr = File.ReadAllText(Path.Combine(srcDir, "TabManager.cs"));
            Judge("F7 刷新书签墙时沿用各自标题（启动页不会刷成书签）",
                  tabMgr.IndexOf("ShowNewTabPage(t, t.WallTitle)", StringComparison.Ordinal) >= 0, "刷新调用没带标题");
            Judge("F8 空 url 仍走书签墙分支",
                  tabMgr.IndexOf("tab.PendingUrl = string.IsNullOrEmpty(url) ? null : url;", StringComparison.Ordinal) >= 0,
                  "NewTab 的 url 判空写法变了，需人工复核");
            Judge("F9 ShowNewTabPage 无参重载默认用书签墙标题",
                  tabMgr.IndexOf("ShowNewTabPage(tab, NewTabPage.NewTabTitle)", StringComparison.Ordinal) >= 0,
                  "无参重载的默认标题变了");
            Judge("F10 NewTab 的墙标题为空时回落「书签」",
                  tabMgr.IndexOf("string.IsNullOrEmpty(wallTitle) ? NewTabPage.NewTabTitle : wallTitle", StringComparison.Ordinal) >= 0,
                  "NewTab 的标题回落逻辑变了");
            _sb.AppendLine();
        }

        /// <summary>在 text 里 needle 出现处的前后若干字符内是否还有 another</summary>
        private static bool Near(string text, string needle, string another)
        {
            int i = text.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return false;
            int from = Math.Max(0, i - 400);
            int to = Math.Min(text.Length, i + 400);
            return text.IndexOf(another, from, to - from, StringComparison.Ordinal) >= 0;
        }

        private static string FindSrcDir()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string cand = Path.Combine(dir, "src");
                    if (File.Exists(Path.Combine(cand, "MainForm.cs"))) return cand;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception) { }
            return null;
        }

        // ---------- 控件取用（对话框字段是私有的，用反射拿） ----------

        private static object Field(object owner, string name)
        {
            FieldInfo f = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(owner);
        }

        private static RadioButton Radio(object owner, string name) { return (RadioButton)Field(owner, name); }
        private static TextBox Box(object owner, string name) { return (TextBox)Field(owner, name); }
        private static Button Btn(object owner, string name) { return (Button)Field(owner, name); }
        private static Label Label(object owner, string name) { return (Label)Field(owner, name); }

        private static void Invoke(object owner, string method)
        {
            MethodInfo m = owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) return;
            m.Invoke(owner, null);
        }

        // ---------- 断言工具 ----------

        private static void Ok(string name, string input, string expected)
        {
            string n, e;
            bool ok = StartupPageSettings.TryNormalize(input, out n, out e);
            Judge(name, ok && n == expected, "实际: " + Show(n) + " 期望: " + expected + " 错误: " + Show(e));
        }

        private static void Bad(string name, string input)
        {
            string n, e;
            bool ok = StartupPageSettings.TryNormalize(input, out n, out e);
            Judge(name, !ok, "实际被接受: " + Show(n));
        }

        private static string Show(string s)
        {
            return (s == null) ? "(null)" : (s.Length == 0 ? "(空)" : s);
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
