using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskbarMemo
{
    public static class Gfx
    {
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;
            if (d <= 0 || bounds.Width < d || bounds.Height < d)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>托盘/标题栏图标：程序内绘制，不依赖外部 .ico 文件。</summary>
    public static class AppIcon
    {
        private static Icon _icon;

        public static Icon Create()
        {
            if (_icon != null) return _icon;

            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        using (GraphicsPath p = Gfx.RoundedRect(new Rectangle(1, 1, 30, 30), 7))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 47, 128, 237)))
                        {
                            g.FillPath(b, p);
                        }

                        using (Pen pen = new Pen(Color.White, 2.4f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, 9f, 11f, 23f, 11f);
                            g.DrawLine(pen, 9f, 16.5f, 23f, 16.5f);
                            g.DrawLine(pen, 9f, 22f, 17f, 22f);
                        }
                    }

                    IntPtr h = bmp.GetHicon();
                    _icon = Icon.FromHandle(h);
                }
            }
            catch
            {
                _icon = SystemIcons.Application;
            }

            return _icon;
        }
    }
}
