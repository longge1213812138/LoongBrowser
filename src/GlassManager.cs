// GlassManager.cs —— 窗口玻璃层：把 Acrylic 模糊应用到顶层窗口
//
// 实测结论（Windows 11 25H2, Build 26200）：
//   · SetWindowCompositionAttribute + ACCENT_ENABLE_ACRYLICBLURBEHIND 返回成功(hr=1)，
//     但窗口内没有任何模糊 —— 返回值不能作为生效证据。
//   · DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE=38) + DwmExtendFrameIntoClientArea(-1)
//     才是真正生效的路径，条纹背景会被真实模糊。
// 详见 docs/adr/0001-use-acrylic-instead-of-aero-glass.md。
//
// 用法：窗口句柄可用后调用 Apply(handle)。失败时静默返回 false，窗口保持不透明外观。
using System;
using System.Runtime.InteropServices;

namespace LoongBrowser
{
    /// <summary>窗口玻璃层：Acrylic 模糊的 P/Invoke 封装</summary>
    public static class GlassManager
    {
        // DWMWA_SYSTEMBACKDROP_TYPE (Windows 11 22H2+)
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // DWMWA_USE_IMMERSIVE_DARK_MODE：让玻璃跟随深/浅色主题
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // DWM_SYSTEMBACKDROP_TYPE 取值
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;          // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3;     // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4;        // Mica Alt

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        /// <summary>玻璃当前是否已成功应用（供测试与诊断查询，不影响外观策略）</summary>
        public static bool IsApplied { get; private set; }

        /// <summary>最近一次应用时 DWM 返回的 HRESULT（0 = 成功）</summary>
        public static int LastHResult { get; private set; }

        /// <summary>
        /// 把 Acrylic 模糊应用到窗口。失败时静默返回 false —— 不做降级策略，
        /// 窗口保持不透明外观即可（见 ADR-0001）。
        /// </summary>
        /// <param name="handle">顶层窗口句柄</param>
        /// <param name="dark">玻璃用深色还是浅色（跟随系统主题）</param>
        public static bool Apply(IntPtr handle, bool dark)
        {
            IsApplied = false;
            LastHResult = -1;
            if (handle == IntPtr.Zero) return false;

            try
            {
                // 1) 把 DWM 边框延伸到整个客户区：
                //    不这么做的话，模糊只出现在非客户区（标题栏），客户区仍然是不透明的一块。
                var margins = new MARGINS();
                margins.Left = -1;
                margins.Right = -1;
                margins.Top = -1;
                margins.Bottom = -1;
                LastHResult = DwmExtendFrameIntoClientArea(handle, ref margins);
                if (LastHResult != 0) return false;

                // 2) 让玻璃跟随系统主题的深/浅
                int darkFlag = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

                // 3) 设置背景材质为 Acrylic（TRANSIENTWINDOW）
                int backdrop = DWMSBT_TRANSIENTWINDOW;
                LastHResult = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                if (LastHResult != 0) return false;

                IsApplied = true;
                return true;
            }
            catch (Exception)
            {
                // 老系统上没有相关导出或调用异常：保持不透明外观
                IsApplied = false;
                return false;
            }
        }

        /// <summary>移除玻璃，恢复不透明窗口</summary>
        public static void Clear(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int backdrop = DWMSBT_NONE;
                DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            catch (Exception) { }
            IsApplied = false;
        }
    }
}
