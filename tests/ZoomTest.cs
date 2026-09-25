// ZoomTest.cs —— 网页缩放的纯逻辑单测：区间约束 / 步进 / 预设档位 / 按键与滚轮判定
// 直接调用真实的 ZoomManager（不复制逻辑），不依赖 WebView2 内核，可在任意机器上跑。
using System;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace LoongBrowser
{
    public static class ZoomTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _sb.AppendLine("=== ZoomManager 缩放逻辑测试 ===");
            _sb.AppendLine("区间: " + ZoomManager.Percent(ZoomManager.MinZoom) +
                           " ~ " + ZoomManager.Percent(ZoomManager.MaxZoom) +
                           "，步进 " + Math.Round(ZoomManager.Step * 100) + "%");
            _sb.AppendLine();

            _sb.AppendLine("-- A. 区间约束 Clamp：任何输入都收进 50%~200% --");
            Num("A1 小于下限 → 触底 50%", ZoomManager.Clamp(0.2), 0.5);
            Num("A2 正好下限", ZoomManager.Clamp(0.5), 0.5);
            Num("A3 区间内原样", ZoomManager.Clamp(1.35), 1.35);
            Num("A4 正好上限", ZoomManager.Clamp(2.0), 2.0);
            Num("A5 大于上限 → 封顶 200%", ZoomManager.Clamp(3.7), 2.0);
            Num("A6 非法值 0 → 100%", ZoomManager.Clamp(0), 1.0);
            Num("A7 负数 → 100%", ZoomManager.Clamp(-1.5), 1.0);
            Num("A8 NaN → 100%", ZoomManager.Clamp(double.NaN), 1.0);
            Num("A9 正无穷 → 100%", ZoomManager.Clamp(double.PositiveInfinity), 1.0);

            _sb.AppendLine();
            _sb.AppendLine("-- B. 归一化 Round：按 1% 取整，吃掉浮点毛刺 --");
            Num("B1 0.6666… → 0.67", ZoomManager.Round(2.0 / 3.0), 0.67);
            Num("B2 0.9999 → 1", ZoomManager.Round(0.9999), 1.0);
            Num("B3 1.004 → 1", ZoomManager.Round(1.004), 1.0);
            Num("B4 越界值先收区间再取整", ZoomManager.Round(9.99), 2.0);

            _sb.AppendLine();
            _sb.AppendLine("-- C. 步进 Up/Down：10% 一级，两端封死 --");
            Num("C1 放大一级", ZoomManager.Up(1.0), 1.1);
            Num("C2 放大到顶后不动", ZoomManager.Up(2.0), 2.0);
            Num("C3 顶附近再放大仍封顶", ZoomManager.Up(1.95), 2.0);
            Num("C4 缩小一级", ZoomManager.Down(1.0), 0.9);
            Num("C5 缩小到底后不动", ZoomManager.Down(0.5), 0.5);
            Num("C6 底附近再缩小仍触底", ZoomManager.Down(0.55), 0.5);
            Num("C7 从 67% 缩小", ZoomManager.Down(0.67), 0.57);
            Num("C8 连续放大 30 次必到 200%", ZoomManager.Up(ZoomManager.Up(ZoomManager.Up(2.0))), 2.0);

            _sb.AppendLine();
            _sb.AppendLine("-- D. 多级步进 StepsFrom：Ctrl+滚轮一格以上 --");
            Num("D1 上 3 级", ZoomManager.StepsFrom(1.0, 3), 1.3);
            Num("D2 下 3 级", ZoomManager.StepsFrom(1.0, -3), 0.7);
            Num("D3 0 级原样", ZoomManager.StepsFrom(1.0, 0), 1.0);
            Num("D4 下越界触底", ZoomManager.StepsFrom(0.5, -2), 0.5);
            Num("D5 上越界封顶", ZoomManager.StepsFrom(2.0, 5), 2.0);
            Num("D6 从 67% 上 1 级", ZoomManager.StepsFrom(0.67, 1), 0.77);
            Num("D7 负向狂滚触底（不回跳 100%）", ZoomManager.StepsFrom(1.0, -10), 0.5);
            Num("D8 负向狂滚从低位同样触底", ZoomManager.StepsFrom(0.67, -9), 0.5);
            Num("D9 正向狂滚封顶", ZoomManager.StepsFrom(1.0, 40), 2.0);

            _sb.AppendLine();
            _sb.AppendLine("-- E. 边界判定 AtMax/AtMin（菜单置灰用）--");
            Bool("E1 200% 已到顶", ZoomManager.AtMax(2.0));
            Bool("E2 190% 未到顶", !ZoomManager.AtMax(1.9));
            Bool("E3 50% 已到底", ZoomManager.AtMin(0.5));
            Bool("E4 60% 未到底", !ZoomManager.AtMin(0.6));
            Bool("E5 越界值按边界算(顶)", ZoomManager.AtMax(5.0));
            Bool("E6 越界值按边界算(底)", ZoomManager.AtMin(0.1));

            _sb.AppendLine();
            _sb.AppendLine("-- F. 显示文本与档位比较 --");
            Str("F1 100% 文本", ZoomManager.Percent(1.0), "100%");
            Str("F2 50% 文本", ZoomManager.Percent(0.5), "50%");
            Str("F3 200% 文本", ZoomManager.Percent(2.0), "200%");
            Str("F4 毛刺比例四舍五入", ZoomManager.Percent(0.666), "67%");
            Str("F5 67% 档位文本", ZoomManager.Percent(0.67), "67%");
            Bool("F6 同档(按 1% 比较)", ZoomManager.SamePercent(1.0, 1.004));
            Bool("F7 不同档", !ZoomManager.SamePercent(0.5, 0.51));
            Bool("F8 档位里没有点号小数", ZoomManager.Percent(1.25).IndexOf('.') < 0);

            _sb.AppendLine();
            _sb.AppendLine("-- G. 预设档位列表 --");
            Bool("G1 覆盖 11 档", ZoomManager.Presets.Length == 11, "实际 " + ZoomManager.Presets.Length);
            Bool("G2 每档都在区间内",
                 AllInRange(), "存在越界档位");
            Bool("G3 由小到大排列", IsAscending(), "顺序不单调");
            Bool("G4 含 100% 默认档", HasPreset(1.0));
            Bool("G5 首档是 50%", ZoomManager.SamePercent(ZoomManager.Presets[0], 0.5));
            Bool("G6 末档是 200%", ZoomManager.SamePercent(ZoomManager.Presets[ZoomManager.Presets.Length - 1], 2.0));

            _sb.AppendLine();
            _sb.AppendLine("-- H. 键盘判定 KeyAction（Ctrl 必须按下）--");
            Act("H1 Ctrl + =", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.Oemplus), ZoomAction.ZoomIn);
            Act("H2 Ctrl + Shift + =（+ 写法，键码相同）", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.Oemplus), ZoomAction.ZoomIn);
            Act("H3 Ctrl + 小键盘 +", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.Add), ZoomAction.ZoomIn);
            Act("H4 Ctrl + -", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.OemMinus), ZoomAction.ZoomOut);
            Act("H5 Ctrl + 小键盘 -", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.Subtract), ZoomAction.ZoomOut);
            Act("H6 Ctrl + 0", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.D0), ZoomAction.ZoomReset);
            Act("H7 Ctrl + 小键盘 0", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.NumPad0), ZoomAction.ZoomReset);
            Act("H8 无 Ctrl 的普通 +", ZoomManager.KeyAction(false, System.Windows.Forms.Keys.Oemplus), ZoomAction.None);
            Act("H9 无 Ctrl 的 0", ZoomManager.KeyAction(false, System.Windows.Forms.Keys.D0), ZoomAction.None);
            Act("H10 Ctrl + C 不受影响", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.C), ZoomAction.None);
            Act("H11 Ctrl + D（暗色模式快捷键）不受影响", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.D), ZoomAction.None);
            Act("H12 Ctrl + Shift + D 不受影响", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.D), ZoomAction.None);
            Act("H13 Ctrl + T 不受影响", ZoomManager.KeyAction(true, System.Windows.Forms.Keys.T), ZoomAction.None);

            _sb.AppendLine();
            _sb.AppendLine("-- I. 滚轮判定 WheelSteps（Ctrl + 滚轮，一格 ±120 = 1 级）--");
            Steps("I1 Ctrl + 上滚一格", ZoomManager.WheelSteps(true, 120), 1);
            Steps("I2 Ctrl + 下滚一格", ZoomManager.WheelSteps(true, -120), -1);
            Steps("I3 Ctrl + 上滚两格", ZoomManager.WheelSteps(true, 240), 2);
            Steps("I4 无 Ctrl → 0（照常滚动）", ZoomManager.WheelSteps(false, 120), 0);
            Steps("I5 位移为 0 → 0", ZoomManager.WheelSteps(true, 0), 0);
            Steps("I6 不足一格的上滚也给 1 级", ZoomManager.WheelSteps(true, 60), 1);
            Steps("I7 不足一格的下滚也给 -1 级", ZoomManager.WheelSteps(true, -60), -1);
            Steps("I8 超大位移封顶 3 级", ZoomManager.WheelSteps(true, 1200), 3);
            Steps("I9 负向超限封顶 -3 级", ZoomManager.WheelSteps(true, -1200), -3);

            _sb.AppendLine();
            _sb.AppendLine("-- J. 会话默认值（新标签页沿用）--");
            double saved = ZoomManager.SessionDefault;
            ZoomManager.SessionDefault = ZoomManager.Round(1.5);
            Num("J1 设置后读回 150%", ZoomManager.SessionDefault, 1.5);
            ZoomManager.SessionDefault = ZoomManager.Clamp(9.0);
            Num("J2 越界默认值被收进 200%", ZoomManager.SessionDefault, 2.0);
            ZoomManager.SessionDefault = saved;

            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        // ---------- 断言工具 ----------

        private static void Num(string name, double actual, double expected)
        {
            bool ok = Math.Abs(actual - expected) < 1e-9;
            Record(ok, name, "实际 " + Fmt(actual) + " | 期望 " + Fmt(expected));
        }

        private static void Str(string name, string actual, string expected)
        {
            Record(actual == expected, name, "实际 " + actual + " | 期望 " + expected);
        }

        private static void Bool(string name, bool ok)
        {
            Record(ok, name, ok ? "" : "结果为 false");
        }

        private static void Bool(string name, bool ok, string extra)
        {
            Record(ok, name, ok ? "" : extra);
        }

        private static void Steps(string name, int actual, int expected)
        {
            Record(actual == expected, name, "实际 " + actual + " | 期望 " + expected);
        }

        private static void Act(string name, ZoomAction actual, ZoomAction expected)
        {
            Record(actual == expected, name, "实际 " + actual + " | 期望 " + expected);
        }

        private static void Record(bool ok, string name, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok) _sb.AppendLine("       " + detail);
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool AllInRange()
        {
            foreach (double p in ZoomManager.Presets)
                if (p < ZoomManager.MinZoom || p > ZoomManager.MaxZoom) return false;
            return true;
        }

        private static bool IsAscending()
        {
            for (int i = 1; i < ZoomManager.Presets.Length; i++)
                if (ZoomManager.Presets[i] <= ZoomManager.Presets[i - 1]) return false;
            return true;
        }

        private static bool HasPreset(double v)
        {
            foreach (double p in ZoomManager.Presets)
                if (ZoomManager.SamePercent(p, v)) return true;
            return false;
        }
    }
}
