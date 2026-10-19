// DefaultBrowser.cs —— 默认浏览器模块：注册表声明 + 引导系统设置
//
// 原理：Windows 10+ 出于安全考虑，不允许程序直接抢占默认浏览器；
// 本模块完成"合法登记"（让 LoongBrowser 出现在系统默认应用列表中），
// 最后一步由用户在系统设置里一键确认。
//
// 登记细节（身份键、ProgId、清理历史遗留）统一交给 FileAssociations ——
// 那里是唯一出口，避免主程序与安装器各写一套、在"打开方式"里冒出多个条目。
using System;
using System.Diagnostics;

namespace LoongBrowser
{
    public static class DefaultBrowser
    {
        /// <summary>写入注册表，将 LoongBrowser 登记为可选浏览器（幂等，并顺手清掉失效的历史条目）</summary>
        public static void Register(string exePath)
        {
            FileAssociations.Register(exePath);
            FileAssociations.RemoveStaleRegistrations();
        }

        /// <summary>撤销全部注册表登记（卸载时用）</summary>
        public static void Unregister()
        {
            // .NET Framework 没有 Environment.ProcessPath，用 WinForms 的等价入口
            string exe = null;
            try { exe = System.Windows.Forms.Application.ExecutablePath; } catch (Exception) { }
            FileAssociations.Unregister(exe);
        }

        /// <summary>打开 Windows"默认应用"设置页，由用户完成最后确认</summary>
        public static void OpenSettings()
        {
            try { Process.Start("ms-settings:defaultapps"); } catch (Exception) { }
        }
    }
}
