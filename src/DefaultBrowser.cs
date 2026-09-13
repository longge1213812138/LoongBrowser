// DefaultBrowser.cs —— 默认浏览器模块：注册表声明 + 引导系统设置
// 原理：Windows 10+ 出于安全考虑，不允许程序直接抢占默认浏览器；
// 本模块完成"合法登记"（让 LoongBrowser 出现在系统默认应用列表中），
// 最后一步由用户在系统设置里一键确认。
using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace LoongBrowser
{
    public static class DefaultBrowser
    {
        private const string ProgId = "LoongBrowser.URL";
        private const string ClientKey = @"Software\Clients\StartMenuInternet\LoongBrowser";

        /// <summary>写入注册表，将 LoongBrowser 登记为可选浏览器</summary>
        public static void Register(string exePath)
        {
            // 1) ProgId：定义打开 http/https 链接的命令（"%1" 接收 URL）
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                k.SetValue("", "LoongBrowser Web Document");
                k.SetValue("FriendlyTypeName", "LoongBrowser Web Document");
                using (var c = k.CreateSubKey(@"shell\open\command"))
                    c.SetValue("", "\"" + exePath + "\" \"%1\"");
                using (var i = k.CreateSubKey("DefaultIcon"))
                    i.SetValue("", exePath + ",0");
            }

            // 2) StartMenuInternet：登记为"已安装的浏览器"
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

            // 3) RegisteredApplications：让系统"默认应用"设置页列出 LoongBrowser
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                k.SetValue("LoongBrowser", ClientKey + @"\Capabilities");
        }

        /// <summary>撤销全部注册表登记（卸载时用）</summary>
        public static void Unregister()
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

        /// <summary>打开 Windows"默认应用"设置页，由用户完成最后确认</summary>
        public static void OpenSettings()
        {
            try { Process.Start("ms-settings:defaultapps"); } catch (Exception) { }
        }
    }
}
