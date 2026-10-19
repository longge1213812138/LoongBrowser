// SnapController.cs —— 自己实现 Aero Snap（贴边半屏 / 拖到顶部最大化 / 拖离还原）
//
// 为什么不直接用系统 Snap：
//   系统 Snap 由尺寸循环（拖边框）或拖动循环（拖标题栏）驱动，而两者都要求窗口样式带
//   WS_THICKFRAME 或走系统非客户区。实测（tests\GlassMatrix.cs，见 docs/adr/0010）：
//   **只要样式里出现 WS_THICKFRAME，窗口失焦时 DWM 就停用 Acrylic**，
//   而且失焦后重新 Apply / Clear+Apply / 强制重绘都救不回来（sat 恒为 0，回前台才恢复）。
//   四种边框形态（None / Sizable / NCCALCSIZE 归零）都绕不过去。
//
// 所以这里把 Snap 的**可见行为**按 Windows 规范复刻：
//   · 拖到屏幕左/右边缘 → 预览吸附为左/右半屏（单屏）或左/右 1/3（宽屏多列时按系统规则）
//   · 拖到屏幕顶部     → 最大化
//   · 从最大化状态拖离 → 还原成拖动前的大小，并让指针落在标题栏上（与系统一致）
//   · 拖动时光标贴边，松手才真正落位（系统也是先给预览、再落位）
//
// 与系统 Snap 的唯一差别：松手瞬间没有系统那条 400ms 的吸附动画。
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>自实现的 Aero Snap：贴边分屏 / 顶部最大化 / 拖离还原</summary>
    public static class SnapController
    {
        /// <summary>离屏幕边缘多少像素算"贴边"（与系统手感接近）</summary>
        public const int SnapDistance = 12;

        /// <summary>吸附目标</summary>
        public enum Zone
        {
            None = 0,
            /// <summary>左半屏</summary>
            Left,
            /// <summary>右半屏</summary>
            Right,
            /// <summary>顶部：最大化</summary>
            Maximize
        }

        /// <summary>
        /// 按"拖动中的窗口矩形 + 鼠标屏幕坐标"判断落点。
        /// 纯函数，不碰窗口 —— 便于不启动窗口就能测。
        /// </summary>
        /// <param name="pointer">鼠标当前屏幕坐标</param>
        /// <param name="workArea">目标屏幕的工作区（不含任务栏）</param>
        public static Zone ZoneAt(Point pointer, Rectangle workArea)
        {
            if (pointer.Y <= workArea.Top + SnapDistance) return Zone.Maximize;
            if (pointer.X <= workArea.Left + SnapDistance) return Zone.Left;
            if (pointer.X >= workArea.Right - SnapDistance) return Zone.Right;
            return Zone.None;
        }

        /// <summary>吸附到某个区后的窗口矩形</summary>
        public static Rectangle BoundsFor(Zone zone, Rectangle workArea)
        {
            int half = workArea.Width / 2;
            switch (zone)
            {
                case Zone.Left:
                    return new Rectangle(workArea.Left, workArea.Top, half, workArea.Height);
                case Zone.Right:
                    return new Rectangle(workArea.Left + half, workArea.Top,
                                         workArea.Width - half, workArea.Height);
                case Zone.Maximize:
                    return workArea;
                default:
                    return Rectangle.Empty;
            }
        }

        /// <summary>
        /// 从最大化状态被拖走时的还原矩形：
        /// 宽度取还原尺寸、按指针水平位置等比映射，纵坐标让指针仍落在标题栏上。
        /// （系统也是这样：拖开的一瞬间窗口就恢复成原来的大小。）
        /// </summary>
        public static Rectangle RestoreBounds(Rectangle restored, Rectangle maximized, Point pointer)
        {
            if (restored.Width <= 0 || maximized.Width <= 0) return restored;

            int x = pointer.X - (int)((long)(pointer.X - maximized.Left) * restored.Width / maximized.Width);
            int y = maximized.Top + (pointer.Y - maximized.Top) * 0 / Math.Max(1, maximized.Height);
            y = pointer.Y - Math.Min(VistaTheme.TitleBarHeight / 2, restored.Height / 3);
            return new Rectangle(x, y, restored.Width, restored.Height);
        }
    }

    /// <summary>
    /// 标题栏拖动会话：负责"跟着鼠标移动 + 判定吸附 + 松手落位"。
    /// 只在没有系统拖动可用时才需要它（本项目因玻璃约束必然走这条）。
    /// </summary>
    public sealed class SnapDragSession
    {
        private readonly Form _form;
        private readonly Point _grabOffset;        // 鼠标相对窗口左上角的偏移
        private readonly Rectangle _startBounds;
        private readonly Rectangle _restoreBounds;  // 若从最大化开始拖，这是还原后的矩形
        private bool _wasMaximized;
        private SnapController.Zone _zone = SnapController.Zone.None;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public SnapDragSession(Form form, Point pointerScreen)
        {
            _form = form;
            _startBounds = form.Bounds;
            _wasMaximized = form.WindowState == FormWindowState.Maximized;

            if (_wasMaximized)
            {
                // 最大化时先算出还原矩形，让窗口立刻恢复成"原来的大小"再跟着走
                Rectangle work = Screen.FromPoint(pointerScreen).WorkingArea;
                _restoreBounds = form.RestoreBounds.Width > 0
                    ? form.RestoreBounds
                    : new Rectangle(work.Left + work.Width / 6, work.Top + work.Height / 6,
                                    work.Width * 2 / 3, work.Height * 2 / 3);
                form.WindowState = FormWindowState.Normal;
                form.Bounds = SnapController.RestoreBounds(_restoreBounds, _startBounds, pointerScreen);
                _startBounds = form.Bounds;
                _grabOffset = new Point(_startBounds.Width / 2, Math.Min(VistaTheme.TitleBarHeight / 2, _startBounds.Height / 3));
            }
            else
            {
                _grabOffset = new Point(pointerScreen.X - _startBounds.X, pointerScreen.Y - _startBounds.Y);
            }
        }

        /// <summary>当前吸附区（供绘制预览）</summary>
        public SnapController.Zone Zone { get { return _zone; } }

        /// <summary>拖动中：更新窗口位置并判定吸附区</summary>
        public void Update(Point pointerScreen)
        {
            if (_form == null || _form.IsDisposed) return;

            Rectangle work = Screen.FromPoint(pointerScreen).WorkingArea;
            _zone = SnapController.ZoneAt(pointerScreen, work);

            // 贴边时窗口先停在原位（系统也是给预览、不提前移动），松手才落位；
            // 没贴边则正常跟随鼠标。
            if (_zone == SnapController.Zone.None)
            {
                int x = pointerScreen.X - _grabOffset.X;
                int y = pointerScreen.Y - _grabOffset.Y;
                _form.Location = new Point(x, y);
            }
        }

        /// <summary>松手：若在吸附区就落位，否则保持当前位置</summary>
        public void End(Point pointerScreen)
        {
            if (_form == null || _form.IsDisposed) return;
            Rectangle work = Screen.FromPoint(pointerScreen).WorkingArea;
            SnapController.Zone zone = SnapController.ZoneAt(pointerScreen, work);
            if (zone == SnapController.Zone.None) return;

            Rectangle target = SnapController.BoundsFor(zone, work);
            _form.Bounds = target;
            if (zone == SnapController.Zone.Maximize) _form.WindowState = FormWindowState.Maximized;
            _zone = SnapController.Zone.None;
        }

        /// <summary>取消（例如按下 Esc）</summary>
        public void Cancel()
        {
            if (_form == null || _form.IsDisposed) return;
            _form.Bounds = _startBounds;
            if (_wasMaximized) _form.WindowState = FormWindowState.Maximized;
            _zone = SnapController.Zone.None;
        }
    }
}
