// NormalizeUrlTest.cs —— 验证 URL 归一化：重点覆盖"双击文件 → 默认程序打开"传入的本地裸路径
// 直接调用真实的 MainForm.NormalizeUrl（不复制逻辑），确保测的就是线上代码。
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LoongBrowser
{
    public static class NormalizeUrlTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            // 测试文件放在工作区内（%TEMP% 可能被沙箱拦截写入）
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string tmp = Path.Combine(baseDir, "tmp");
            string realFile = Path.Combine(tmp, "double_click.html");
            string prep = "";
            try
            {
                Directory.CreateDirectory(tmp);
                File.WriteAllText(realFile, "<html><head><title>DoubleClick OK</title></head><body>hi</body></html>");
                Directory.SetCurrentDirectory(tmp);
                prep = "已创建并切换工作目录：" + Directory.GetCurrentDirectory();
            }
            catch (Exception ex)
            {
                prep = "准备失败：" + ex.GetType().Name + " " + ex.Message;
            }

            _sb.AppendLine("=== NormalizeUrl 归一化测试 ===");
            _sb.AppendLine("测试文件: " + realFile);
            _sb.AppendLine("文件存在: " + File.Exists(realFile) + " | " + prep);
            _sb.AppendLine();

            _sb.AppendLine("-- A. 核心场景：双击 .html（注册表命令 \"exe\" \"%1\" 传入裸路径）--");
            Exact("A1 典型盘符路径", @"C:\Users\91533\Desktop\test.html",
                  "file:///C:/Users/91533/Desktop/test.html");
            Exact("A2 小写盘符", @"c:\temp\a.html", "file:///c:/temp/a.html");
            Exact("A3 正斜杠路径", "C:/Users/91533/a.htm", "file:///C:/Users/91533/a.htm");
            Exact("A4 路径含空格(转义)", @"C:\My Docs\my page.html",
                  "file:///C:/My%20Docs/my%20page.html");
            NotContains("A5 路径含空格(不得出现裸空格)", @"C:\My Docs\my page.html", " ");
            Contains("A6 中文路径", @"C:\资料\页面.html", "%E8%B5%84%E6%96%99");
            RoundTrip("A7 工作区内的真实文件", realFile, realFile);
            RoundTrip("A8 相对路径(文件存在)", "double_click.html", realFile);
            Exact("A9 带残留引号", "\"C:\\Users\\a.html\"", "file:///C:/Users/a.html");
            Contains("A10 UNC 网络路径", @"\\server\share\a.html", "file:", "server/share/a.html");
            Exact("A11 目录也能打开", @"C:\Users", "file:///C:/Users");

            _sb.AppendLine();
            _sb.AppendLine("-- B. 回归：原有行为不能被破坏 --");
            Exact("B1 https 链接", "https://example.com/a.html", "https://example.com/a.html");
            Exact("B2 http 链接", "http://example.com/x", "http://example.com/x");
            Exact("B3 域名补协议", "example.com", "https://example.com");
            Exact("B4 带路径的域名", "www.qq.com/news", "https://www.qq.com/news");
            Exact("B5 无点号 → 搜索", "hello", "https://www.bing.com/search?q=hello");
            Exact("B6 含空格 → 搜索", "hello world", "https://www.bing.com/search?q=hello%20world");
            Exact("B7 中文搜索", "天气预报", "https://www.bing.com/search?q=" + Uri.EscapeDataString("天气预报"));
            Exact("B8 about:", "about:blank", "about:blank");
            Exact("B9 file: 原样透传", "file:///C:/x.html", "file:///C:/x.html");
            Exact("B10 localhost 原样", "localhost:8080", "localhost:8080");
            Exact("B11 空输入", "", "about:blank");
            Exact("B12 已存在的非路径输入不受影响", "www.baidu.com", "https://www.baidu.com");

            _sb.AppendLine();
            _sb.AppendLine("-- C. 机理复现：旧代码为何报 ERR_NAME_NOT_RESOLVED --");
            string legacy = "https://" + @"C:\Users\91533\Desktop\test.html";
            Info("C1 旧代码拼出的导航目标", legacy);
            string authority = ExtractAuthority(legacy);
            Info("C1 该 URL 的\"主机名\"段", "<" + authority + ">");
            Judge("C1 盘符 C 被当成主机名（= 报错里的那个 c）",
                  authority.TrimEnd(':').Equals("C", StringComparison.OrdinalIgnoreCase));
            try
            {
                Uri u = new Uri(legacy);
                Info("C1 .NET 解析", "Host=<" + u.Host + ">");
            }
            catch (Exception ex)
            {
                Info("C1 .NET 解析", ex.GetType().Name + "：" + ex.Message + "（证实 C: 被当作 host:port）");
            }
            Exact("C2 修复后同一输入", @"C:\Users\91533\Desktop\test.html",
                  "file:///C:/Users/91533/Desktop/test.html");

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>取 URL 中 :// 之后、第一个路径分隔符之前的部分（即 host[:port]）</summary>
        private static string ExtractAuthority(string url)
        {
            int i = url.IndexOf("//", StringComparison.Ordinal);
            if (i < 0) return url;
            i += 2;
            int j = url.IndexOfAny(new char[] { '/', '\\' }, i);
            return (j < 0) ? url.Substring(i) : url.Substring(i, j - i);
        }

        // ---------- 断言工具 ----------

        private static void Exact(string name, string input, string expected)
        {
            string actual = MainForm.NormalizeUrl(input);
            Record(actual == expected, name, input, actual, "期望: " + expected);
        }

        private static void Contains(string name, string input, params string[] needles)
        {
            string actual = MainForm.NormalizeUrl(input);
            bool ok = true;
            for (int i = 0; i < needles.Length; i++)
                if (actual.IndexOf(needles[i], StringComparison.Ordinal) < 0) ok = false;
            Record(ok, name, input, actual, "应包含: " + string.Join(" | ", needles));
        }

        private static void NotContains(string name, string input, string needle)
        {
            string actual = MainForm.NormalizeUrl(input);
            Record(actual.IndexOf(needle, StringComparison.Ordinal) < 0, name, input, actual,
                   "不应包含: " + needle);
        }

        /// <summary>往返校验：输出的 file URL 解码后必须等于原始文件路径</summary>
        private static void RoundTrip(string name, string input, string expectedPath)
        {
            string actual = MainForm.NormalizeUrl(input);
            string decoded = "";
            try { decoded = Uri.UnescapeDataString(actual); } catch (Exception) { }
            string expected = "file:///" + expectedPath.Replace('\\', '/');
            Record(decoded == expected, name, input, actual, "解码后期望: " + expected);
        }

        private static void Judge(string name, bool ok)
        {
            Record(ok, name, "(构造)", ok ? "—" : "—", ok ? "" : "机理复现未成立，需人工复核");
        }

        private static void Info(string label, string value)
        {
            _sb.AppendLine("       * " + label + ": " + value);
        }

        private static void Record(bool ok, string name, string input, string actual, string extra)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (input != "(构造)")
            {
                _sb.AppendLine("       输入: " + input);
                _sb.AppendLine("       输出: " + actual);
            }
            if (!ok && extra.Length > 0) _sb.AppendLine("       " + extra);
        }
    }
}
