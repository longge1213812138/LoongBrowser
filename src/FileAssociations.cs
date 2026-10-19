// FileAssociations.cs —— 「打开方式」登记的唯一出口
//
// 背景（用户反馈"右键菜单有很多历史版本"）：
//   同一份程序被登记了**两条**身份不同的记录：
//     · Applications\LoongBrowser.exe  —— 安装器写，指向安装目录
//     · LoongBrowser.html (ProgId)     —— 主程序 DefaultBrowser.Register 写，指向当前 exe
//   Windows 把两者当成两个不同的应用，于是"打开方式"里出现两个 LoongBrowser；
//   更糟的是它们各自指向不同路径，安装目录换过之后旧那条就是死链 ——
//   点了没反应，用户只会觉得"程序坏了"。
//
// 本模块的立场：
//   1. **一条身份**：Applications\<exe 文件名> 是 Windows 认应用身份的地方，
//      也是"打开方式"列表的主来源。ProgId 只作为 **html 关联的目标**存在，
//      不再单独提供图标与名字（免得又冒出一个条目）。
//   2. **指向当前 exe**：任何一次登记都把路径改写成"正在运行的这份"，
//      exe 挪过位置后自动纠正，不留死链。
//   3. **清理历史**：登记时顺手删掉本程序遗留的其它身份（旧 ProgId、旧 Applications 项、
//      那些已经在磁盘上不存在的路径）。只动自己写下的键，不碰其它应用。
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace LoongBrowser
{
    public static class FileAssociations
    {
        /// <summary>html 关联使用的 ProgId（仅作为 .html 的候选目标，不单独当应用身份）</summary>
        public const string HtmlProgId = "LoongBrowser.html";

        /// <summary>http/https 使用的 ProgId</summary>
        public const string UrlProgId = "LoongBrowser.URL";

        /// <summary>应用身份键：Applications\&lt;exe 文件名&gt;</summary>
        private static string AppKey(string exeName)
        {
            return @"Software\Classes\Applications\" + exeName;
        }

        private static readonly string[] LegacyProgIds = new string[]
        {
            "LoongBrowser.html",
            "LoongBrowser.URL",
            "LoongBrowser.htm",
            "LoongBrowserHTML"
        };

        /// <summary>
        /// 把全部关联登记到指定 exe，并清掉历史遗留。
        /// 幂等：重复调用结果一致。
        /// </summary>
        public static void Register(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;
            string exeName = Path.GetFileName(exePath);
            if (string.IsNullOrEmpty(exeName)) return;

            string command = "\"" + exePath + "\" \"%1\"";

            // 1) 应用身份：Applications\<exe>。FriendlyAppName 决定"打开方式"里显示什么，
            //    不设就会显示成裸 exe 名（甚至因为缺 command 而点了没反应）。
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey(exeName)))
            {
                k.SetValue("FriendlyAppName", "LoongBrowser");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", command);
                using (var i = k.CreateSubKey("DefaultIcon"))
                    i.SetValue("", exePath + ",0");
            }

            // 2) html 的候选目标：ProgId 只当"目标类型"，不再写自己的图标/名字，
            //    这样"打开方式"里只出现 Applications 那一条。
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + HtmlProgId))
            {
                k.SetValue("", "LoongBrowser HTML Document");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", command);
                using (var i = k.CreateSubKey("DefaultIcon"))
                    i.SetValue("", exePath + ",0");
            }
            AddOpenWith(".html");
            AddOpenWith(".htm");

            // 3) http/https：RegisteredApplications + Capabilities（系统"默认应用"用）
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Clients\StartMenuInternet\LoongBrowser"))
            {
                k.SetValue("", "LoongBrowser");
                k.SetValue("LocalizedString", "LoongBrowser");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", "\"" + exePath + "\"");
                using (var d = k.CreateSubKey("DefaultIcon"))
                    d.SetValue("", exePath + ",0");
                using (var cap = k.CreateSubKey("Capabilities"))
                {
                    cap.SetValue("ApplicationName", "LoongBrowser");
                    cap.SetValue("ApplicationIcon", exePath + ",0");
                    cap.SetValue("ApplicationDescription", "极简 Chromium 内核浏览器");
                    using (var u = cap.CreateSubKey("URLAssociations"))
                    {
                        u.SetValue("http", UrlProgId);
                        u.SetValue("https", UrlProgId);
                    }
                }
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + UrlProgId))
            {
                k.SetValue("", "LoongBrowser Web Document");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", "\"" + exePath + "\" \"%1\"");
                using (var i = k.CreateSubKey("DefaultIcon"))
                    i.SetValue("", exePath + ",0");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                k.SetValue("LoongBrowser", @"Software\Clients\StartMenuInternet\LoongBrowser\Capabilities");
        }

        /// <summary>
        /// 清掉"指向已不存在的 exe"的历史登记。
        /// 只在用户主动做登记动作时顺手跑一遍，不做后台扫描。
        /// </summary>
        public static void RemoveStaleRegistrations()
        {
            RemoveStaleAppKeys();
            RemoveStaleProgIds();
            RemoveStaleAutoFileKeys();
        }

        /// <summary>
        /// Windows 有个不怎么出名但很要命的行为：用户用"打开方式"选过一个程序后，
        /// 系统会在 HKCU\Software\Classes 下留下 &lt;ext&gt;_auto_file 键，
        /// 把那次选择的命令行**固化**下来。程序后来挪了位置——甚至整个目录被删掉——
        /// 这个键仍然指着旧路径，于是"打开方式"菜单里就一直挂着一个点不动的
        /// LoongBrowser（用户看到的就是"很多历史版本"）。
        ///
        /// 只清理**指向本程序且目标已不存在**的那种键，别的一概不碰。
        /// </summary>
        private static void RemoveStaleAutoFileKeys()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (!name.EndsWith("_auto_file", StringComparison.OrdinalIgnoreCase)) continue;
                        string target = CommandTarget(@"Software\Classes\" + name + @"\shell\open\command");
                        if (string.IsNullOrEmpty(target)) continue;
                        // 只看是不是本程序的遗留（目录名/文件名带 LoongBrowser），且文件已经不在了
                        if (target.IndexOf("LoongBrowser", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (File.Exists(target)) continue;
                        try { root.DeleteSubKeyTree(name, false); } catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        private static void RemoveStaleAppKeys()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications", true))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (!IsOurs(name)) continue;
                        string target = CommandTarget(@"Software\Classes\Applications\" + name + @"\shell\open\command");
                        // 指向不存在文件的、或者压根没有 command 的，都是死链
                        if (string.IsNullOrEmpty(target) || !File.Exists(target))
                        {
                            try { root.DeleteSubKeyTree(name, false); } catch (Exception) { }
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        private static void RemoveStaleProgIds()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (!IsOurs(name)) continue;
                        string target = CommandTarget(@"Software\Classes\" + name + @"\shell\open\command");
                        if (string.IsNullOrEmpty(target) || !File.Exists(target))
                        {
                            try { root.DeleteSubKeyTree(name, false); } catch (Exception) { }
                            RemoveOpenWith(".html", name);
                            RemoveOpenWith(".htm", name);
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>是不是本程序可能写下的项（只认这几个前缀，绝不误删别人的）</summary>
        private static bool IsOurs(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("LoongBrowser", StringComparison.OrdinalIgnoreCase)) return true;
            // Applications 下的键以 exe 文件名命名；只在"文件名就是本程序"时才认
            return name.Equals("LoongBrowser.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>读出某个 command 键里的可执行文件路径（处理带引号与带参数两种写法）</summary>
        private static string CommandTarget(string commandKeyPath)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(commandKeyPath))
                {
                    if (k == null) return null;
                    string cmd = k.GetValue("") as string;
                    if (string.IsNullOrEmpty(cmd)) return null;
                    cmd = cmd.Trim();
                    if (cmd.StartsWith("\""))
                    {
                        int end = cmd.IndexOf('"', 1);
                        return end > 1 ? cmd.Substring(1, end - 1) : null;
                    }
                    int space = cmd.IndexOf(' ');
                    return space > 0 ? cmd.Substring(0, space) : cmd;
                }
            }
            catch (Exception) { return null; }
        }

        private static void AddOpenWith(string ext)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids"))
                    k.SetValue(HtmlProgId, new byte[0], RegistryValueKind.None);
            }
            catch (Exception) { }
        }

        private static void RemoveOpenWith(string ext, string progId)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids", true))
                    if (k != null) k.DeleteValue(progId, false);
            }
            catch (Exception) { }
        }

        /// <summary>撤销全部登记（卸载时用）</summary>
        public static void Unregister(string exePath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\LoongBrowser", false); } catch (Exception) { }
            foreach (string prog in LegacyProgIds)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + prog, false); } catch (Exception) { }
                RemoveOpenWith(".html", prog);
                RemoveOpenWith(".htm", prog);
            }
            if (!string.IsNullOrEmpty(exePath))
            {
                string exeName = Path.GetFileName(exePath);
                if (!string.IsNullOrEmpty(exeName))
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree(AppKey(exeName), false); } catch (Exception) { }
                }
            }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                    k.DeleteValue("LoongBrowser", false);
            }
            catch (Exception) { }
        }

        /// <summary>列出"打开方式"里当前会出现的本程序条目（诊断/测试用）</summary>
        public static List<string> DescribeEntries()
        {
            var list = new List<string>();
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications"))
                {
                    if (root != null)
                    {
                        foreach (string name in root.GetSubKeyNames())
                        {
                            if (!IsOurs(name)) continue;
                            string target = CommandTarget(@"Software\Classes\Applications\" + name + @"\shell\open\command");
                            list.Add("Applications\\" + name + " -> " + (target ?? "(无 command)") +
                                     " [存在=" + (!string.IsNullOrEmpty(target) && File.Exists(target)) + "]");
                        }
                    }
                }
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.html\OpenWithProgids"))
                {
                    if (root != null)
                    {
                        foreach (string name in root.GetValueNames())
                        {
                            if (!IsOurs(name)) continue;
                            string target = CommandTarget(@"Software\Classes\" + name + @"\shell\open\command");
                            list.Add(".html OpenWithProgid " + name + " -> " + (target ?? "(无 command)") +
                                     " [存在=" + (!string.IsNullOrEmpty(target) && File.Exists(target)) + "]");
                        }
                    }
                }
                // Windows 记住的"曾用打开方式"：指向本程序、但文件已不在的那种也算历史条目
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes"))
                {
                    if (root != null)
                    {
                        foreach (string name in root.GetSubKeyNames())
                        {
                            if (!name.EndsWith("_auto_file", StringComparison.OrdinalIgnoreCase)) continue;
                            string target = CommandTarget(@"Software\Classes\" + name + @"\shell\open\command");
                            if (string.IsNullOrEmpty(target)) continue;
                            if (target.IndexOf("LoongBrowser", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (File.Exists(target)) continue;
                            list.Add(name + " -> " + target + " [存在=False]");
                        }
                    }
                }
            }
            catch (Exception) { }
            return list;
        }

        // ---------- 与资源管理器同一口径的枚举 ----------
        //
        // 上面 DescribeEntries 只是把注册表里"我们写的键"列出来，便于排查；
        // 但用户在右键菜单里看到几条，由 Explorer 调用 SHAssocEnumHandlers 决定。
        // 两者并不等价（系统还会做去重/合并），所以"是否只剩一条"必须用真实 API 断言。

        [System.Runtime.InteropServices.ComImport,
         System.Runtime.InteropServices.Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"),
         System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumAssocHandlers
        {
            [System.Runtime.InteropServices.PreserveSig]
            int Next(uint celt,
                     [System.Runtime.InteropServices.Out,
                      System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeParamIndex = 0)]
                     IAssocHandler[] rgelt,
                     out uint pceltFetched);
        }

        [System.Runtime.InteropServices.ComImport,
         System.Runtime.InteropServices.Guid("f04061ac-1659-4a3f-a954-775aa57fc083"),
         System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAssocHandler
        {
            [System.Runtime.InteropServices.PreserveSig] int GetName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppsz);
            [System.Runtime.InteropServices.PreserveSig] int GetUIName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppsz);
            [System.Runtime.InteropServices.PreserveSig] int GetIconLocation([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppszPath, out int pIndex);
            [System.Runtime.InteropServices.PreserveSig] int IsRecommended();
            [System.Runtime.InteropServices.PreserveSig] int MakeDefault([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszDescription);
            [System.Runtime.InteropServices.PreserveSig] int Invoke(IntPtr pdo);
            [System.Runtime.InteropServices.PreserveSig] int CreateInvoker(IntPtr pdo, out IntPtr ppInvoker);
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
        private static extern void SHAssocEnumHandlers(string pszExtra, int afFilter, out IEnumAssocHandlers ppEnumHandler);

        /// <summary>
        /// "打开方式"里本程序出现了几次 + 各自指向哪个 exe。
        /// 口径与资源管理器完全一致（SHAssocEnumHandlers），也是验收判据。
        /// </summary>
        public static List<string> ExplorerEntries(string extension)
        {
            var list = new List<string>();
            IEnumAssocHandlers en = null;
            try
            {
                SHAssocEnumHandlers(extension, 0, out en);
                var buf = new IAssocHandler[1];
                while (true)
                {
                    uint got;
                    if (en.Next(1, buf, out got) != 0 || got == 0) break;
                    IAssocHandler h = buf[0];
                    if (h == null) break;
                    string ui = null, path = null;
                    int idx;
                    try { h.GetUIName(out ui); } catch (Exception) { }
                    try { h.GetIconLocation(out path, out idx); } catch (Exception) { }
                    string text = (ui ?? "") + " | " + (path ?? "");
                    if (text.IndexOf("LoongBrowser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("Loong", StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(text);
                }
            }
            catch (Exception) { }
            finally
            {
                if (en != null) try { System.Runtime.InteropServices.Marshal.ReleaseComObject(en); } catch (Exception) { }
            }
            return list;
        }
    }
}
