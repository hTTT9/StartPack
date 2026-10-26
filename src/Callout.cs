// Подсказка-выноска со стрелкой вниз: «Совет: …» и две кнопки. Показывается поверх окна над нужным элементом.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StartPack
{
    public class Callout : Control
    {
        readonly string title, text;
        readonly FlatBtn yes, no;
        float sc = 1f;
        public int ArrowX;                  // где стрелка (от левого края выноски)
        public event EventHandler Accept, Dismiss;

        int S(int v) { return (int)Math.Round(v * sc); }
        int Arrow { get { return S(9); } }
        int Pad { get { return S(16); } }

        public Callout(string title, string text, string yesText, string noText)
        {
            this.title = title;
            this.text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            BackColor = Theme.Current.Surface;

            yes = new FlatBtn(yesText, BtnKind.Primary, null) { Compact = true };
            yes.Fit();
            yes.Click += delegate { if (Accept != null) Accept(this, EventArgs.Empty); };
            no = new FlatBtn(noText, BtnKind.Secondary, null) { Compact = true };
            no.Fit();
            no.Click += delegate { if (Dismiss != null) Dismiss(this, EventArgs.Empty); };
            Controls.Add(yes);
            Controls.Add(no);

            int w = S(380);
            int textW = w - 2 * Pad;
            int th = TextRenderer.MeasureText(title, Fonts.Semi(10.5f)).Height;
            int bh = TextRenderer.MeasureText(text, Fonts.Ui(9.5f), new Size(textW, 0), TextFormatFlags.WordBreak).Height;
            int by = Pad + th + S(6) + bh + S(14);
            Size = new Size(w, by + yes.Height + Pad + Arrow);
            yes.Location = new Point(w - Pad - yes.Width, by);
            no.Location = new Point(yes.Left - S(8) - no.Width, by);
            ArrowX = w - S(40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(1, 1, Width - 3, Height - Arrow - 2);
            using (GraphicsPath p = Gfx.Round(box, S(12)))
            {
                // стрелка к переключателю
                float ax = Math.Max(S(20), Math.Min(Width - S(20), ArrowX));
                using (var arrow = new GraphicsPath())
                {
                    arrow.AddPolygon(new[] { new PointF(ax - Arrow, box.Bottom - 1), new PointF(ax + Arrow, box.Bottom - 1), new PointF(ax, box.Bottom + Arrow - 1) });
                    using (var b = new SolidBrush(t.Surface)) { g.FillPath(b, p); g.FillPath(b, arrow); }
                    using (var pen = new Pen(t.Amber, 1.6f * sc))
                    {
                        g.DrawPath(pen, p);
                        g.DrawLine(pen, ax - Arrow, box.Bottom, ax, box.Bottom + Arrow - 1);
                        g.DrawLine(pen, ax, box.Bottom + Arrow - 1, ax + Arrow, box.Bottom);
                    }
                    using (var b = new SolidBrush(t.Surface)) g.FillRectangle(b, ax - Arrow + 2 * sc, box.Bottom - 2 * sc, 2 * Arrow - 4 * sc, 2.5f * sc);
                }
            }
            int th = TextRenderer.MeasureText(title, Fonts.Semi(10.5f)).Height;
            Gfx.Text(g, title, Fonts.Semi(10.5f), new Rectangle(Pad, Pad, Width - 2 * Pad, th), t.Text, TextFormatFlags.SingleLine);
            Gfx.Text(g, text, Fonts.Ui(9.5f), new Rectangle(Pad, Pad + th + S(6), Width - 2 * Pad, Height), t.Text, TextFormatFlags.WordBreak);
        }
    }
}
