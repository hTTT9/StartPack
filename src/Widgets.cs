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

        // Ширина всех таблеток — чтобы родитель мог прижать полосу вправо.
        public int NeededWidth()
        {
            int w = 0;
            foreach (NetTarget t in Targets) w += TextRenderer.MeasureText(Label(t), F).Width + S(34) + S(8);
            return Math.Max(0, w - S(8));
        }

        void Layout2()
        {
            pills.Clear();
            int x = Width - NeededWidth();
            foreach (NetTarget t in Targets)
            {
                int w = TextRenderer.MeasureText(Label(t), F).Width + S(34);
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
                int d = S(8);
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + S(12), r.Y + (r.Height - d) / 2, d, d);
                Gfx.Text(g, Label(t), F, new Rectangle(r.X + S(26), r.Y, r.Width - S(30), r.Height), t.State == "fail" ? th.Red : th.Text,
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
