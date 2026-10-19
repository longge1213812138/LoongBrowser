// FileAssociationsTest.cs —— "打开方式"里只能有一个 LoongBrowser
//
// 判据用 Explorer 自己的 API（SHAssocEnumHandlers）：它列出来的就是用户在
// 右键 → 打开方式 里看到的东西。注册表快照只作诊断，不作为通过标准 ——
// 两者并不等价，用自建视图当判据会得出错误结论（本项目踩过）。
//
// 用户症状：右键菜单里有很多个 LoongBrowser（历史版本）。
// 三个来源：
//   1. Applications\<exe>            —— 应用身份（安装器写）
//   2. <ext>\OpenWithProgids + ProgId —— HTML 关联目标（主程序写）
//   3. <ext>_auto_file               —— Windows 记住的"曾用打开方式"（用一次留一条，
//                                      程序挪位置后就成了点不动的死条目）
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace LoongBrowser
{
    public static class FileAssociationsTest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;
        private static string _fakeExe;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _sb.AppendLine("=== 「打开方式」条目唯一性（Explorer 真实枚举）===");

            string dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assoc_tmp");
            Directory.CreateDirectory(dir);
            _fakeExe = Path.Combine(dir, "LoongBrowser.exe");
            File.WriteAllText(_fakeExe, "stub");

            _sb.AppendLine("-- 清理前：Explorer 枚举 --");
            foreach (string s in FileAssociations.ExplorerEntries(".html")) _sb.AppendLine("   " + s);

            // 1) 清历史遗留，再登记到当前 exe
            FileAssociations.RemoveStaleRegistrations();
            FileAssociations.Register(_fakeExe);

            var entries = FileAssociations.ExplorerEntries(".html");
            _sb.AppendLine("-- 登记后：Explorer 枚举 --");
            foreach (string s in entries) _sb.AppendLine("   " + s);

            Judge("A1 打开方式里恰好一条 LoongBrowser", entries.Count == 1,
                  "实际 " + entries.Count + " 条：" + string.Join(" | ", entries.ToArray()));
            Judge("A2 那一条指向本次登记的 exe",
                  entries.Count == 1 && entries[0].IndexOf(_fakeExe, StringComparison.OrdinalIgnoreCase) >= 0,
                  entries.Count == 0 ? "(无条目)" : entries[0]);

            // 2) 幂等
            FileAssociations.Register(_fakeExe);
            Judge("B1 重复登记后仍然只有一条",
                  FileAssociations.ExplorerEntries(".html").Count == 1,
                  "实际 " + FileAssociations.ExplorerEntries(".html").Count + " 条");

            // 3) 制造历史遗留：三种死链都塞进去
            Seed(@"Software\Classes\Applications\LoongBrowser.exe\shell\open\command",
                 @"C:\__no_such_dir__\LoongBrowser.exe");
            Seed(@"Software\Classes\html_auto_file\shell\open\command",
                 @"C:\__no_such_dir2__\LoongBrowser.exe");
            Seed(@"Software\Classes\htm_auto_file\shell\open\command",
                 @"C:\__no_such_dir3__\LoongBrowser.exe");

            int dirty = FileAssociations.ExplorerEntries(".html").Count;
            _sb.AppendLine("       * 塞入 3 条历史遗留后，Explorer 仍看到 " + dirty + " 条");
            Judge("C1 历史遗留确实会制造出多条（复现成立）", dirty > 1, "只看到 " + dirty + " 条，未复现");

            // 4) 清理后回到一条
            FileAssociations.RemoveStaleRegistrations();
            var cleaned = FileAssociations.ExplorerEntries(".html");
            _sb.AppendLine("-- 清理后：Explorer 枚举 --");
            foreach (string s in cleaned) _sb.AppendLine("   " + s);
            Judge("C2 清理后回到恰好一条", cleaned.Count == 1,
                  "实际 " + cleaned.Count + " 条：" + string.Join(" | ", cleaned.ToArray()));

            // 5) 别人的应用不受影响
            var foreign = ForeignApps();
            Judge("D1 其它应用的 Applications 条目未被删除", foreign.Count >= 1,
                  "一个外部条目都找不到，可能误删了别人的键");
            _sb.AppendLine("       * 保留的外部条目: " + string.Join(", ", foreign.ToArray()));

            // 6) htm 也只剩一条（两个扩展名各查一遍，别只修 html）
            var htm = FileAssociations.ExplorerEntries(".htm");
            Judge("E1 .htm 的打开方式里也只有一条 LoongBrowser",
                  htm.Count <= 1, "实际 " + htm.Count + " 条：" + string.Join(" | ", htm.ToArray()));

            // 收尾：指向真实 exe，别给用户留假路径
            string realExe = FindRealExe();
            if (realExe != null) FileAssociations.Register(realExe);
            Judge("F1 收尾：仍是恰好一条",
                  FileAssociations.ExplorerEntries(".html").Count == 1,
                  "实际 " + FileAssociations.ExplorerEntries(".html").Count + " 条");

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            try { Directory.Delete(dir, true); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        private static string FindRealExe()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string cand = Path.Combine(dir, "dist", "LoongBrowser.exe");
                    if (File.Exists(cand)) return cand;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception) { }
            return null;
        }

        private static void Seed(string keyPath, string deadExe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(keyPath))
                k.SetValue("", "\"" + deadExe + "\" \"%1\"");
        }

        private static List<string> ForeignApps()
        {
            var list = new List<string>();
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications"))
                {
                    if (root == null) return list;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (name.IndexOf("Loong", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        list.Add(name);
                    }
                }
            }
            catch (Exception) { }
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
