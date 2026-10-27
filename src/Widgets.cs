// Строка «связь»: таблетки YouTube / Discord / Spotify / Telegram с цветной точкой.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StartPack
{
    public class NetBar : Control
    {
        public List<NetTarget> Targets = new List<NetTarget>();
        public event EventHandler Recheck;
        float sc = 1f;
        int hover = -1;
        readonly ToolTip tip = new ToolTip { InitialDelay = 300 };
        string tipShown;
        readonly List<Rectangle> pills = new List<Rectangle>();

        int S(int v) { return (int)Math.Round(v * sc); }

        public NetBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Cursor = Cursors.Hand;
        }

        Font F { get { return Fonts.Semi(9.5f); } }

        string Label(NetTarget t)
        {
            if (t.State == "ok") return t.Name + "  " + t.Ms + " мс";
            if (t.State == "fail") return t.Name + "  нет связи";
            if (t.State == "checking") return t.Name + "  …";
            return t.Name;
        }

        // Ширина таблетки — под самую длинную надпись («нет связи», «999 мс»),
        // чтобы во время проверки строка не прыгала и не уезжала под соседние кнопки.
        int PillW(NetTarget t)
        {
            int w = 0;
            foreach (string s in new[] { t.Name + "  нет связи", t.Name + "  999 мс", Label(t) })
                w = Math.Max(w, TextRenderer.MeasureText(s, F).Width);
            return w + S(34);
        }

        // Ширина всех таблеток — чтобы родитель мог прижать полосу вправо.
        public int NeededWidth()
        {
            int w = 0;
            foreach (NetTarget t in Targets) w += PillW(t) + S(8);
            return Math.Max(0, w - S(8));
        }

        void Layout2()
        {
            pills.Clear();
            int x = Math.Max(0, Width - NeededWidth());
            foreach (NetTarget t in Targets)
            {
                int w = PillW(t);
                pills.Add(new Rectangle(x, (Height - S(30)) / 2, w, S(30)));
                x += w + S(8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Layout2();
            for (int i = 0; i < Targets.Count; i++)
            {
                NetTarget t = Targets[i];
                Rectangle r = pills[i];
                Gfx.Fill(g, r, r.Height / 2f, i == hover ? th.SurfaceAlt : th.Surface);
                Gfx.Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f, th.Border, 1f);
                Color dot = t.State == "ok" ? th.Green : t.State == "fail" ? th.Red : t.State == "checking" ? th.Blue : th.TextFaint;
                // точка и надпись — по центру таблетки (ширина у таблетки постоянная)
                int d = S(8);
                int tw = TextRenderer.MeasureText(Label(t), F, Size.Empty, TextFormatFlags.NoPadding).Width;
                int cx = r.X + (r.Width - (d + S(6) + tw)) / 2;
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, cx, r.Y + (r.Height - d) / 2, d, d);
                Gfx.Text(g, Label(t), F, new Rectangle(cx + d + S(6), r.Y, tw + S(4), r.Height), t.State == "fail" ? th.Red : th.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = pills.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
            string tt = null;
            if (h >= 0)
            {
                NetTarget t = Targets[h];
                tt = t.State == "ok" ? t.Name + " отвечает (" + t.Ms + " мс). Нажмите, чтобы проверить ещё раз."
                   : t.State == "fail" ? t.Name + ": " + t.Error + ". Нажмите, чтобы проверить ещё раз."
                   : "Проверить связь с " + t.Name;
            }
            if (tt != tipShown) { tipShown = tt; tip.SetToolTip(this, tt ?? ""); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (pills.FindIndex(r => r.Contains(e.Location)) >= 0 && Recheck != null) Recheck(this, EventArgs.Empty);
        }
    }
}
