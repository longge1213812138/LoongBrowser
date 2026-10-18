// GlassManager.cs —— 窗口玻璃层：把 Acrylic 模糊应用到顶层窗口
//
// 实测结论（Windows 11 25H2, Build 26200）：
//   · SetWindowCompositionAttribute + ACCENT_ENABLE_ACRYLICBLURBEHIND 返回成功(hr=1)，
//     但窗口内没有任何模糊 —— 返回值不能作为生效证据。
//   · DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE=38) + DwmExtendFrameIntoClientArea(-1)
//     才是真正生效的路径，条纹背景会被真实模糊。
// 详见 docs/adr/0001-use-acrylic-instead-of-aero-glass.md。
//
// 时机（实测，2026-10）：窗口**可见之前**设的 backdrop 会被窗口创建的后续流程覆盖，
// 首次显示出来是不透明的一块，之后任何一次重绘才「突然」出现玻璃。因此 Apply 必须在
// 窗口真正显示之后调用（MainForm.OnShown），并且要可重复调用（重复调用无副作用）。
//
// 用法：窗口显示后调用 Apply(handle)。失败时静默返回 false，窗口保持不透明外观。
using System;
using System.Drawing;
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

        // GDI 光栅操作：BLACKNESS 把目标像素置为 0（扩展边框的客户区里黑色即"透明"）
        private const int BLACKNESS = 0x00000042;

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

        [DllImport("gdi32.dll")]
        private static extern bool PatBlt(IntPtr hdc, int x, int y, int width, int height, int rop);

        /// <summary>玻璃当前是否已成功应用（供测试与诊断查询，不影响外观策略）</summary>
        public static bool IsApplied { get; private set; }

        /// <summary>最近一次应用时 DWM 返回的 HRESULT（0 = 成功）</summary>
        public static int LastHResult { get; private set; }

        /// <summary>累计清面次数（供测试核对"每帧都先清面"这一不变量）</summary>
        public static int SurfaceClearCount { get; private set; }


        /// <summary>
        /// 仅测试用：非 Color.Empty 时清面改用该颜色填充，而不是 PatBlt。
        /// </summary>
        /// <remarks>
        /// 生产路径永远是 PatBlt(BLACKNESS) —— 只有它能让 DWM 把像素重新当透明。
        /// 但"画成纯黑"在离屏合成图里无法与前景区分，测试（ChromePreviewTest）需要
        /// 一块可辨认的标记色才能把"透明区"换成模拟桌面再核对前景。
        /// </remarks>
        public static Color PreviewClearColor = Color.Empty;

        /// <summary>
        /// 把 Acrylic 模糊应用到窗口。失败时静默返回 false —— 不做降级策略，
        /// 窗口保持不透明外观即可（见 ADR-0001）。
        /// </summary>
        /// <param name="handle">顶层窗口句柄（窗口必须已经显示）</param>
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

                // 3) 先退回 NONE 再设 Acrylic：把材质设成"与当前相同"时 DWM 可能直接忽略，
                //    重复 Apply（句柄重建、从全屏还原）就不会重新合成。两步走保证它真的重设一次。
                int none = DWMSBT_NONE;
                DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

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

        /// <summary>
        /// 把一块绘制面清成"透明"（玻璃透出）。
        /// </summary>
        /// <remarks>
        /// Opaque + 不画背景的控件是"往上叠加"，不是"每帧重来"：GDI 只在需要重绘的矩形里
        /// 落笔，没落笔的像素保留上一次的内容。于是地址栏从宽变窄、窗口缩小之后，旧位置的
        /// 白条还留在窗口表面（用户看到的"地址栏瑕疵"），鼠标移上去让它局部重绘也只是
        /// 越描越乱。
        /// 扩展边框后的客户区里，把像素置 0（BLACKNESS）就是"这里什么都没有"，DWM 会重新
        /// 用 Acrylic 合成 —— GDI 无法直接写 alpha，这是唯一能把已画过的像素擦回透明的办法。
        /// 裁剪区要放开：OnPaint 的 Graphics 只裁在失效矩形上，只清那一块解决不了残留；
        /// 而且清面必然是整块的，调用方必须紧跟一次整块重画，所以这里不恢复裁剪区。
        /// </remarks>
        public static void ClearSurface(Graphics g, int width, int height)
        {
            ClearSurface(g, new Rectangle(0, 0, width, height));
        }

        /// <summary>把指定矩形清成"透明"（只清这一块时用，例如窗口四周的缩放边带）</summary>
        public static void ClearSurface(Graphics g, Rectangle bounds)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            SurfaceClearCount++;

            Region saved = g.Clip;
            IntPtr hdc = IntPtr.Zero;
            try
            {
                g.ResetClip();
                // 清面是"整块"的，所以绘制也必须整块重来 —— 调用方紧接着要把这一整块
                // 重新画一遍。裁剪区就此放开、不再恢复：若仍按失效矩形裁剪，被清掉的
                // 只会在失效矩形内重画，矩形外的前景（按钮、标题）会凭空消失。
                if (PreviewClearColor != Color.Empty)
                {
                    using (var brush = new SolidBrush(PreviewClearColor))
                        g.FillRectangle(brush, bounds);
                }
                else
                {
                    hdc = g.GetHdc();
                    PatBlt(hdc, bounds.X, bounds.Y, bounds.Width, bounds.Height, BLACKNESS);
                }
            }
            catch (Exception)
            {
                // 清面失败就退回普通绘制：外观略脏，但不影响交互
            }
            finally
            {
                if (hdc != IntPtr.Zero) g.ReleaseHdc(hdc);
                if (saved != null) saved.Dispose();
            }
        }
    }
}
