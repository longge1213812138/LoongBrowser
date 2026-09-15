// Installer.cs —— LoongBrowser 安装器（独立编译为 LoongBrowserSetup.exe）
// 功能：自定义路径安装 / 卸载 / 注册表登记 / 快捷方式 / WebView2 运行时检测
// 用法：直接运行打开界面；/install 静默安装（默认路径）；/uninstall 静默卸载
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LoongBrowserSetup
{
    public static class Program
    {
        public const string AppName = "LoongBrowser";
        public const string AppTitle = "LoongBrowser 浏览器";
        public const string ProgId = "LoongBrowser.URL";
        public const string ClientKey = @"Software\Clients\StartMenuInternet\LoongBrowser";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LoongBrowser";

        public static string DefaultInstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "LoongBrowser");
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string mode = (args != null && args.Length > 0) ? args[0].Trim().TrimStart('/', '-').ToLowerInvariant() : "";
            if (mode == "uninstall")
            {
                DoUninstall(true, null);
                return;
            }
            if (mode == "install")
            {
                DoInstall(false, DefaultInstallDir);
                return;
            }
            Application.EnableVisualStyles();
            Application.Run(new InstallerForm());
        }

        // ---------- 安装 ----------

        public static bool DoInstall(bool interactive, string targetDir)
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir)) targetDir = DefaultInstallDir;
                targetDir = Path.GetFullPath(targetDir);
                if (targetDir.TrimEnd('\\').ToLowerInvariant() == "c:\\" ||
                    targetDir.TrimEnd('\\').ToLowerInvariant() == Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant())
                {
                    if (interactive) MessageBox.Show("安装路径不合法，请选择其他目录。", "错误");
                    return false;
                }

                string srcDir = AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(targetDir);

                string[] files = new string[]
                {
                    "LoongBrowser.exe",
                    "Microsoft.Web.WebView2.Core.dll",
                    "Microsoft.Web.WebView2.WinForms.dll",
                    "WebView2Loader.dll"
                };
                foreach (string f in files)
                {
                    string s = Path.Combine(srcDir, f);
                    if (File.Exists(s)) File.Copy(s, Path.Combine(targetDir, f), true);
                }

                string exe = Path.Combine(targetDir, "LoongBrowser.exe");
                if (!File.Exists(exe))
                {
                    if (interactive) MessageBox.Show("未找到 LoongBrowser.exe，安装包不完整。", "错误");
                    return false;
                }

                // 注册表：默认浏览器登记 + 卸载信息
                RegisterBrowser(exe);
                string setupPath = Assembly.GetExecutingAssembly().Location;
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppTitle + " (LoongBrowser)");
                    k.SetValue("DisplayVersion", "1.0.0");
                    k.SetValue("InstallLocation", targetDir);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("UninstallString", "\"" + setupPath + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("Publisher", "LoongBrowser");
                }

                // 快捷方式
                string startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                CreateShortcut(startMenu, AppTitle + ".lnk", exe);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(desktop, AppTitle + ".lnk", exe);

                if (interactive)
                {
                    if (!IsWebView2RuntimePresent())
                    {
                        var dr = MessageBox.Show(
                            "未检测到 Microsoft WebView2 运行时（浏览器内核组件）。\n" +
                            "是否前往微软官网下载安装？（约 2MB 引导器，装一次全系统通用）",
                            "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (dr == DialogResult.Yes)
                        {
                            try { Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703"); } catch (Exception) { }
                        }
                    }

                    var dr2 = MessageBox.Show(
                        "安装完成！" +
                        "\n\n是否现在将 LoongBrowser 设为默认浏览器？",
                        "安装完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr2 == DialogResult.Yes)
                    {
                        DefaultBrowserOpenSettings();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (interactive) MessageBox.Show("安装失败：" + ex.Message, "错误");
                return false;
            }
        }

        // ---------- 卸载 ----------

        public static bool DoUninstall(bool interactive, string customDir)
        {
            try
            {
                if (interactive)
                {
                    var dr = MessageBox.Show(
                        "确定要卸载 " + AppTitle + " 吗？\n（书签等用户数据将被一并删除）",
                        "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return false;
                }

                // 结束运行中的浏览器进程
                foreach (var p in Process.GetProcessesByName("LoongBrowser"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
                }

                UnregisterBrowser();

                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey)) { }
                    Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                }
                catch (Exception) { }

                // 快捷方式
                string startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                TryDeleteFile(Path.Combine(startMenu, AppTitle + ".lnk"));
                TryDeleteFile(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppTitle + ".lnk"));

                // 程序文件
                string dir = !string.IsNullOrEmpty(customDir) ? customDir : GetInstalledDir();
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    TryDeleteDir(dir);

                // 用户数据
                TryDeleteDir(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoongBrowser"));
                TryDeleteDir(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoongBrowser"));

                if (interactive) MessageBox.Show("卸载完成。", AppTitle);
                return true;
            }
            catch (Exception ex)
            {
                if (interactive) MessageBox.Show("卸载失败：" + ex.Message, "错误");
                return false;
            }
        }

        // ---------- 注册表 ----------

        private static void RegisterBrowser(string exePath)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                k.SetValue("", "LoongBrowser Web Document");
                k.SetValue("FriendlyTypeName", "LoongBrowser Web Document");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", "\"" + exePath + "\" \"%1\"");
                using (var i = k.CreateSubKey("DefaultIcon"))
                    i.SetValue("", exePath + ",0");
            }

            using (var k = Registry.CurrentUser.CreateSubKey(ClientKey))
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
                        u.SetValue("http", ProgId);
                        u.SetValue("https", ProgId);
                    }
                }
            }

            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                k.SetValue("LoongBrowser", ClientKey + @"\Capabilities");
        }

        private static void UnregisterBrowser()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\LoongBrowser", false); } catch (Exception) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\LoongBrowser.URL", false); } catch (Exception) { }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                    k.DeleteValue("LoongBrowser", false);
            }
            catch (Exception) { }
        }

        public static void DefaultBrowserOpenSettings()
        {
            try { Process.Start("ms-settings:defaultapps"); } catch (Exception) { }
        }

        // ---------- 工具 ----------

        public static string GetInstalledDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    if (k != null)
                    {
                        var v = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
            catch (Exception) { }
            return DefaultInstallDir;
        }

        public static bool IsWebView2RuntimePresent()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
                    if (k != null && k.GetValue("pv") != null && k.GetValue("pv").ToString().Length > 0) return true;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
                    if (k != null && k.GetValue("pv") != null && k.GetValue("pv").ToString().Length > 0) return true;
            }
            catch (Exception) { }
            return false;
        }

        private static void CreateShortcut(string dir, string name, string target)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = shell.CreateShortcut(Path.Combine(dir, name));
                lnk.TargetPath = target;
                lnk.WorkingDirectory = Path.GetDirectoryName(target);
                lnk.Description = AppTitle;
                lnk.Save();
            }
            catch (Exception) { }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception) { }
        }
    }

    /// <summary>安装器主界面：选择安装位置 → 安装 / 卸载</summary>
    public class InstallerForm : Form
    {
        private TextBox _dirBox;

        public InstallerForm()
        {
            Text = Program.AppTitle + " 安装程序";
            Size = new Size(560, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;

            var label = new Label();
            label.Text = Program.AppTitle + " v1.0.1\n基于 Chromium 内核 (WebView2)\n\n选择安装位置：";
            label.Location = new Point(18, 14);
            label.Size = new Size(510, 66);
            label.AutoSize = false;

            _dirBox = new TextBox();
            _dirBox.Text = Program.DefaultInstallDir;
            _dirBox.Location = new Point(18, 86);
            _dirBox.Width = 440;

            var btnBrowse = new Button();
            btnBrowse.Text = "浏览...";
            btnBrowse.Location = new Point(466, 84);
            btnBrowse.Size = new Size(64, 24);
            btnBrowse.Click += delegate
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = _dirBox.Text;
                    dlg.Description = "选择 LoongBrowser 的安装位置";
                    if (dlg.ShowDialog(this) == DialogResult.OK) _dirBox.Text = dlg.SelectedPath;
                }
            };

            var btnInstall = new Button();
            btnInstall.Text = "安装";
            btnInstall.Location = new Point(90, 140);
            btnInstall.Size = new Size(110, 36);
            btnInstall.Click += delegate
            {
                if (Program.DoInstall(true, _dirBox.Text.Trim())) Close();
            };

            var btnUninstall = new Button();
            btnUninstall.Text = "卸载";
            btnUninstall.Location = new Point(230, 140);
            btnUninstall.Size = new Size(110, 36);
            btnUninstall.Click += delegate
            {
                if (Program.DoUninstall(true, _dirBox.Text.Trim())) Close();
            };

            var btnClose = new Button();
            btnClose.Text = "退出";
            btnClose.Location = new Point(370, 140);
            btnClose.Size = new Size(110, 36);
            btnClose.Click += delegate { Close(); };

            Controls.Add(label);
            Controls.Add(_dirBox);
            Controls.Add(btnBrowse);
            Controls.Add(btnInstall);
            Controls.Add(btnUninstall);
            Controls.Add(btnClose);
        }
    }
}
