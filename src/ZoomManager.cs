// ZoomManager.cs —— 网页缩放：50%~200% 的区间约束、步进、预设档位，以及按键/滚轮判定
//
// WebView2 的缩放入口是 WebView2 控件上的 ZoomFactor（底层为内核控制器的 ZoomFactor，
// 对当前标签生效且换页不丢）。本类只做“纯计算 + 一次写入”，不持有任何 UI 状态，方便单测直接调用：
//   · Clamp/Round/Up/Down/StepsFrom —— 比例永远收在 [50%, 200%]，且按 1% 取整（避免 0.66999… 这类浮点毛刺）
//   · KeyAction/WheelSteps —— 键盘（Ctrl+= / Ctrl+- / Ctrl+0）与 Ctrl+滚轮的判定，
//     主窗口的消息过滤器只负责把系统消息翻译成这两个函数的入参。
using System;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    /// <summary>一次缩放指令的类型（按键 / 滚轮判定的结果）</summary>
    public enum ZoomAction
    {
        /// <summary>不是缩放按键，交回系统处理</summary>
        None,
        /// <summary>放大一级</summary>
        ZoomIn,
        /// <summary>缩小一级</summary>
        ZoomOut,
        /// <summary>恢复 100%</summary>
        ZoomReset
    }

    public static class ZoomManager
    {
        /// <summary>最小缩放比例（50%）</summary>
        public const double MinZoom = 0.5;

        /// <summary>最大缩放比例（200%）</summary>
        public const double MaxZoom = 2.0;

        /// <summary>默认比例（100%）</summary>
        public const double DefaultZoom = 1.0;

        /// <summary>每级步进（10%）：放大/缩小一次、滚轮一格都走这一级</summary>
        public const double Step = 0.1;

        /// <summary>菜单与工具栏里可直接点选的预设档位（50% ~ 200%，由小到大）</summary>
        public static readonly double[] Presets = new double[]
        {
            0.50, 0.67, 0.75, 0.80, 0.90, 1.00, 1.10, 1.25, 1.50, 1.75, 2.00
        };

        /// <summary>
        /// 会话默认比例：用户调过缩放后，新开标签页与弹出小窗都沿用它（只在内存里，不落盘）。
        /// </summary>
        public static double SessionDefault = DefaultZoom;

        /// <summary>把任意比例收进 50%~200%；非法值（NaN / 非正数）回落到 100%</summary>
        public static double Clamp(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0) return DefaultZoom;
            if (factor < MinZoom) return MinZoom;
            if (factor > MaxZoom) return MaxZoom;
            return factor;
        }

        /// <summary>收进区间后按 1% 取整（比较、显示、写内核前都先归一化）</summary>
        public static double Round(double factor)
        {
            return Math.Round(Clamp(factor) * 100.0) / 100.0;
        }

        /// <summary>放大一级（到 200% 封顶）</summary>
        public static double Up(double current)
        {
            return Round(Clamp(current) + Step);
        }

        /// <summary>缩小一级（到 50% 触底）</summary>
        public static double Down(double current)
        {
            return Round(Clamp(current) - Step);
        }

        /// <summary>按 n 级步进移动（n 可为负、可大于 1，用于 Ctrl+滚轮一次多格）</summary>
        public static double StepsFrom(double current, int steps)
        {
            double cur = Round(current);
            if (steps == 0) return cur;
            double value = cur + Step * steps;
            // 连续下滚可能把结果算成 0 或负数：这种情况要触底 50%，
            // 不能走 Round —— 它会把“非法值”当 100% 兜底，反而跳回默认缩放
            if (value <= 0) return MinZoom;
            return Round(value);
        }

        /// <summary>已经到 200%（用于置灰「放大」）</summary>
        public static bool AtMax(double factor)
        {
            return Round(factor) >= Round(MaxZoom);
        }

        /// <summary>已经到 50%（用于置灰「缩小」）</summary>
        public static bool AtMin(double factor)
        {
            return Round(factor) <= Round(MinZoom);
        }

        /// <summary>比例 → 显示文本，如 1.0 → "100%"（固定用点号小数，不受系统区域影响）</summary>
        public static string Percent(double factor)
        {
            return Math.Round(Clamp(factor) * 100.0).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>两个比例是否是同一档（按 1% 比较，给菜单勾选用）</summary>
        public static bool SamePercent(double a, double b)
        {
            return Math.Round(Clamp(a) * 100.0) == Math.Round(Clamp(b) * 100.0);
        }

        /// <summary>把比例写进某个网页视图；内核未就绪或属性异常时静默忽略</summary>
        public static void Apply(WebView2 view, double factor)
        {
            if (view == null || view.CoreWebView2 == null) return;
            try { view.ZoomFactor = Clamp(factor); }
            catch (Exception) { }
        }

        /// <summary>读取某个网页视图的缩放比例（内核未就绪时返回会话默认值）</summary>
        public static double FactorOf(WebView2 view)
        {
            if (view == null || view.CoreWebView2 == null) return SessionDefault;
            try { return Round(view.ZoomFactor); }
            catch (Exception) { return SessionDefault; }
        }

        /// <summary>
        /// 键盘判定：Ctrl 按下时 —— Ctrl+=（含 Ctrl+Shift+= 的加号写法）与小键盘 Ctrl+ 放大，
        /// Ctrl+- / 小键盘 Ctrl- 缩小，Ctrl+0 / 小键盘 Ctrl+0 恢复 100%。其余组合一律 None。
        /// 注意入参必须先用 Keys.KeyCode 去掉修饰键位（调用方负责）。
        /// </summary>
        public static ZoomAction KeyAction(bool control, Keys keyCode)
        {
            if (!control) return ZoomAction.None;
            switch (keyCode)
            {
                case Keys.Oemplus:
                case Keys.Add:
                    return ZoomAction.ZoomIn;
                case Keys.OemMinus:
                case Keys.Subtract:
                    return ZoomAction.ZoomOut;
                case Keys.D0:
                case Keys.NumPad0:
                    return ZoomAction.ZoomReset;
                default:
                    return ZoomAction.None;
            }
        }

        /// <summary>
        /// Ctrl+滚轮判定：非 Ctrl 或没有有效位移 → 0（照常滚动）；
        /// 否则返回要走的级数（一格 = 120 = 一级，方向跟滚轮一致，单次最多 3 级）。
        /// </summary>
        public static int WheelSteps(bool control, int delta)
        {
            if (!control || delta == 0) return 0;
            int steps = delta / 120;
            if (steps == 0) steps = delta > 0 ? 1 : -1;     // 不足一格的细碎位移也按方向给一级
            if (steps > 3) steps = 3;
            if (steps < -3) steps = -3;
            return steps;
        }
    }
}
