using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TaskbarMemo
{
    /// <summary>一个分区的显示状态。</summary>
    public class SectionData
    {
        public MemoKind Kind;
        public string Text;
        public int Index;   // 1 基
        public int Total;
    }

    public class SectionEventArgs : EventArgs
    {
        public readonly MemoKind Kind;

        public SectionEventArgs(MemoKind kind)
        {
            Kind = kind;
        }
    }

    /// <summary>
    /// 备忘录显示窗件。三种类别各占一个分区，同一行内并列显示。
    ///
    ///   Compact = true  → 任务栏条状：三个分区横向排列，单行
    ///   Compact = false → 悬浮卡片：三个分区各占一行
    ///
    /// 双击时按点击位置判断作用于哪个分区。
    /// </summary>
    public class MemoForm : Form
    {
        private const int CardRadius = 12;
        private const int BarRadius = 4;

        private readonly Font _tagFont = UiFont.Get(8f, FontStyle.Bold);
        private readonly Font _barFont = UiFont.Get(9.5f, FontStyle.Regular);
        private readonly Font _hintFont = UiFont.Get(7.5f, FontStyle.Regular);
        private readonly Font _emptyFont = UiFont.Get(10f, FontStyle.Regular);

        private SectionData[] _sections = new SectionData[0];
        private Rectangle[] _rects = new Rectangle[0];

        private string _emptyText = "";
        private string _hintText = "双击完成该分区 · 右键菜单";
        private bool _isEmpty = true;

        private bool _compact;
        private int _radius = CardRadius;
        private Color _bg = Color.FromArgb(26, 28, 32);
        private Color _fg = Color.FromArgb(246, 250, 251, 253);
        private Color _dim = Color.FromArgb(150, 200, 206, 214);

        /// <summary>双击了某个分区。</summary>
        public event EventHandler<SectionEventArgs> CompleteSectionRequested;

        /// <summary>右键要菜单。</summary>
        public event EventHandler MenuRequested;

        public MemoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = _bg;
            ClientSize = new Size(300, 64);
            DoubleBuffered = true;
            Text = "任务栏备忘";

            try
            {
                Icon = AppIcon.Create();
            }
            catch
            {
            }
        }

        // 显示时不抢焦点（开机自启时不打断用户当前操作）。
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public bool Compact
        {
            get { return _compact; }
        }

        /// <summary>卡片模式在给定分区数下应有的高度。</summary>
        public static int CardHeight(int rows)
        {
            if (rows < 1) rows = 1;
            if (rows > 3) rows = 3;
            return 12 + rows * 26 + 26;
        }

        public void SetMode(bool compact, Color bg, Color fg, Color dim)
        {
            if (_compact == compact && _bg == bg && _fg == fg && _dim == dim) return;

            _compact = compact;
            _bg = bg;
            _fg = fg;
            _dim = dim;
            _radius = compact ? BarRadius : CardRadius;

            BackColor = bg;
            ApplyRegion();
            Invalidate();
        }

        /// <summary>悬浮模式：贴到主显示器工作区右下角。</summary>
        public void SnapToCorner()
        {
            Rectangle wa;
            try
            {
                wa = Screen.PrimaryScreen.WorkingArea;
            }
            catch
            {
                return;
            }

            int x = wa.Right - Width - 12;
            int y = wa.Bottom - Height - 12;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            Location = new Point(x, y);
        }

        public void ShowSections(SectionData[] sections)
        {
            _sections = sections == null ? new SectionData[0] : sections;
            _isEmpty = _sections.Length == 0;
            _rects = new Rectangle[_sections.Length];
            Invalidate();
        }

        public void ShowEmpty(string message)
        {
            _sections = new SectionData[0];
            _rects = new Rectangle[0];
            _emptyText = message == null ? "" : message;
            _isEmpty = true;
            Invalidate();
        }

        public static string TagOf(MemoKind kind)
        {
            if (kind == MemoKind.Routine) return "常规";
            if (kind == MemoKind.Future) return "远期";
            return "自定义";
        }

        public static Color ColorOf(MemoKind kind)
        {
            if (kind == MemoKind.Routine) return Color.FromArgb(255, 250, 201, 92);
            if (kind == MemoKind.Future) return Color.FromArgb(255, 122, 220, 150);
            return Color.FromArgb(255, 108, 178, 255);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath p = Gfx.RoundedRect(new Rectangle(0, 0, Width, Height), _radius))
                {
                    System.Drawing.Region old = this.Region;
                    this.Region = new System.Drawing.Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            if (r.Width < 20 || r.Height < 20) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_compact)
            {
                PaintBar(g, r);
            }
            else
            {
                PaintCard(g, r);
            }

            base.OnPaint(e);
        }

        // ------------------------------------------------------------ 任务栏条状

        private void PaintBar(Graphics g, Rectangle r)
        {
            using (SolidBrush bg = new SolidBrush(_bg))
            {
                g.FillRectangle(bg, r);
            }

            if (_isEmpty || _sections.Length == 0)
            {
                _rects = new Rectangle[0];
                string line = TextWrap.FirstLine(_emptyText, _barFont, r.Width - 24);
                using (SolidBrush fb = new SolidBrush(_dim))
                {
                    float y = (r.Height - _barFont.GetHeight(g)) / 2f;
                    g.DrawString(line, _barFont, fb, 12f, y);
                }
                return;
            }

            int n = _sections.Length;
            _rects = new Rectangle[n];

            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = (i == n - 1) ? (r.Width - x) : (r.Width / n);
                if (w < 1) w = 1;

                Rectangle cell = new Rectangle(x, 0, w, r.Height);
                _rects[i] = cell;
                PaintBarCell(g, cell, _sections[i]);
                x += w;
            }
        }

        private void PaintBarCell(Graphics g, Rectangle cell, SectionData s)
        {
            if (s == null || cell.Width < 6) return;

            Color accent = ColorOf(s.Kind);

            using (SolidBrush ab = new SolidBrush(accent))
            {
                g.FillRectangle(ab, cell.X, cell.Y, 2, cell.Height);
            }

            int pad = 8;
            int counterW = 0;
            string counter = s.Total > 1 ? (s.Index + "/" + s.Total) : "";

            if (counter.Length > 0 && cell.Width > 96)
            {
                SizeF cs = g.MeasureString(counter, _hintFont);
                counterW = (int)cs.Width + 6;
                using (SolidBrush cb = new SolidBrush(_dim))
                {
                    float cy = cell.Y + (cell.Height - _hintFont.GetHeight(g)) / 2f;
                    g.DrawString(counter, _hintFont, cb, cell.Right - cs.Width - 4f, cy);
                }
            }

            int textWidth = cell.Width - pad - counterW - 8;
            if (textWidth < 12) return;

            string text = TextWrap.FirstLine(s.Text, _barFont, textWidth);
            using (SolidBrush fb = new SolidBrush(_fg))
            {
                float y = cell.Y + (cell.Height - _barFont.GetHeight(g)) / 2f;
                g.DrawString(text, _barFont, fb, cell.X + pad, y);
            }
        }

        // ------------------------------------------------------------ 悬浮卡片

        private void PaintCard(Graphics g, Rectangle r)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Rectangle(0, 0, r.Width, r.Height + 1),
                Color.FromArgb(44, 48, 56), Color.FromArgb(22, 24, 28), 90f))
            {
                g.FillRectangle(bg, r);
            }

            using (GraphicsPath edge = Gfx.RoundedRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), CardRadius))
            using (Pen pen = new Pen(Color.FromArgb(56, 255, 255, 255), 1f))
            {
                g.DrawPath(pen, edge);
            }

            if (_isEmpty || _sections.Length == 0)
            {
                _rects = new Rectangle[0];
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush eb = new SolidBrush(Color.FromArgb(165, 200, 206, 214)))
                    {
                        g.DrawString(_emptyText, _emptyFont, eb,
                            new RectangleF(0f, 8f, r.Width, r.Height - 16f), sf);
                    }
                }
                return;
            }

            int n = _sections.Length;
            _rects = new Rectangle[n];

            for (int i = 0; i < n; i++)
            {
                Rectangle row = new Rectangle(0, 12 + i * 26, r.Width, 26);
                _rects[i] = row;
                PaintCardRow(g, row, _sections[i]);
            }

            if (_hintText.Length > 0)
            {
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(115, 200, 206, 214)))
                {
                    g.DrawString(_hintText, _hintFont, hb, 14f, r.Height - 18f);
                }
            }
        }

        private void PaintCardRow(Graphics g, Rectangle row, SectionData s)
        {
            if (s == null) return;

            Color c = ColorOf(s.Kind);

            using (SolidBrush ab = new SolidBrush(c))
            {
                g.FillRectangle(ab, 14, row.Y + 5, 3, 15);
            }

            string tag = TagOf(s.Kind);
            SizeF ts = g.MeasureString(tag, _tagFont);
            using (SolidBrush tb = new SolidBrush(c))
            {
                g.DrawString(tag, _tagFont, tb, 22f, row.Y + 5f);
            }

            int counterW = 0;
            string counter = s.Total > 1 ? (s.Index + "/" + s.Total) : "";
            if (counter.Length > 0)
            {
                SizeF cs = g.MeasureString(counter, _hintFont);
                counterW = (int)cs.Width + 10;
                using (SolidBrush cb = new SolidBrush(Color.FromArgb(140, 200, 206, 214)))
                {
                    g.DrawString(counter, _hintFont, cb, row.Right - 14 - cs.Width, row.Y + 7f);
                }
            }

            int textX = 22 + (int)ts.Width + 8;
            int textW = row.Right - 14 - counterW - textX;
            if (textW < 20) return;

            string text = TextWrap.FirstLine(s.Text, _barFont, textW);
            using (SolidBrush fb = new SolidBrush(_fg))
            {
                g.DrawString(text, _barFont, fb, textX, row.Y + 5f);
            }
        }

        // ------------------------------------------------------------ 鼠标

        private MemoKind? HitSection(Point p)
        {
            if (_isEmpty || _rects == null) return null;
            for (int i = 0; i < _rects.Length; i++)
            {
                if (_rects[i].Contains(p) && _sections != null && i < _sections.Length)
                {
                    return _sections[i].Kind;
                }
            }
            return null;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                MemoKind? kind = HitSection(e.Location);
                if (kind != null)
                {
                    EventHandler<SectionEventArgs> h = CompleteSectionRequested;
                    if (h != null) h(this, new SectionEventArgs(kind.Value));
                }
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                EventHandler h = MenuRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }
    }
}
