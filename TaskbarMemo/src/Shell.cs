using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarMemo
{
    /// <summary>
    /// 任务栏几何探测。用来把窗件塞进任务栏内部的空白区域。
    ///
    /// 说明：这不是 deskband（真正的 shell 扩展），而是在任务栏的空白位置
    /// 画一个置顶小窗。视觉上属于任务栏，且不遮挡任何应用窗口。
    /// 任何一步探测失败都返回 false，调用方退回悬浮模式。
    /// </summary>
    internal static class Shell
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter,
            string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HwndTopMost = new IntPtr(-1);

        /// <summary>
        /// 周期性把窗口重新声明为最顶层。
        ///
        /// 为什么需要：任务栏 Shell_TrayWnd 自己也是 topmost 窗口。我们把备忘画在
        /// 任务栏区域里，两个 topmost 窗口叠在一起时，谁在激活序列里更新谁就浮上来。
        /// 用户切到浏览器等窗口时任务栏被提升，就会把备忘盖住，表现为"备忘消失了"。
        /// Form.TopMost 只在创建时生效一次，挡不住这种后续的层级变化。
        ///
        /// 不移动、不改大小、不抢焦点，所以用户不会看到闪烁或焦点跳动。
        /// </summary>
        public static bool RaiseTopMost(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            const uint swpNoSize = 0x0001;
            const uint swpNoMove = 0x0002;
            const uint swpNoActivate = 0x0010;

            try
            {
                return SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0,
                    swpNoSize | swpNoMove | swpNoActivate);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>任务栏是深色还是浅色（跟随系统主题）。</summary>
        public static bool TaskbarIsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SystemUsesLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            }
            catch
            {
            }
            return true;
        }

        /// <summary>
        /// 算出任务栏里可用的一条矩形（托盘左侧、任务栏高度内）。
        /// 竖向任务栏、自动隐藏任务栏、找不到窗口，都返回 false。
        /// </summary>
        public static bool TryGetTaskbarSlot(int desiredWidth, out Rectangle slot)
        {
            slot = Rectangle.Empty;

            try
            {
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return false;

                RECT tb;
                if (!GetWindowRect(taskbar, out tb)) return false;

                int tbWidth = tb.Right - tb.Left;
                int tbHeight = tb.Bottom - tb.Top;
                if (tbWidth <= 0 || tbHeight <= 0) return false;

                // 竖向任务栏（贴在屏幕左右两侧）不做处理，退回悬浮模式
                if (tbHeight >= tbWidth) return false;

                Rectangle screen = Screen.PrimaryScreen.Bounds;

                // 自动隐藏：任务栏被顶到屏幕外，此时不要把窗件放在那里
                if (tb.Top >= screen.Bottom - 2) return false;
                if (tb.Top + tbHeight <= screen.Top) return false;

                // 托盘左边界作为我们的右边界
                int rightLimit = tb.Right - 4;
                IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                if (tray != IntPtr.Zero)
                {
                    RECT tr;
                    if (GetWindowRect(tray, out tr) && tr.Left > tb.Left) rightLimit = tr.Left - 4;
                }

                int available = rightLimit - (tb.Left + 8);
                if (available < 120) return false; // 没地方了，别硬塞

                int w = desiredWidth;
                if (w > available) w = available;
                if (w < 120) return false;

                int h = tbHeight - 8;
                if (h < 22) h = 22;
                if (h > tbHeight) h = tbHeight;

                int x = rightLimit - w;
                int y = tb.Top + (tbHeight - h) / 2;

                slot = new Rectangle(x, y, w, h);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
