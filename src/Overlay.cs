// Оверлей: маленькая полупрозрачная панель поверх всех окон — что запущено, VPN, связь.
// Кнопки ▶/■ у программ и ↻ у связи, размер, что показывать — «Настроить оверлей» (OverlaySettings.cs).
// Перетаскивается мышью, Ctrl+Alt+O — показать/скрыть. Ползунок (Slider) — для настройки прозрачности.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StartPack
{
    public class OverlayRow
    {
        public string Name, Status;
        public Color Dot;
        public object Tag;          // карточка
        public bool Running;        // работает — кнопка ■, иначе ▶
        public bool CanAct;         // есть что запустить/остановить
    }

    public class OverlayForm : Form
    {
        List<OverlayRow> rows = new List<OverlayRow>();
        List<NetTarget> net = new List<NetTarget>();
        bool clickThrough, showStatus = true, showMs = true, buttons = true, netBusy;
        float dpi = 1f, k = 1f;
        Image logo;
        readonly System.Windows.Forms.Timer moveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        readonly List<KeyValuePair<Rectangle, string>> hot = new List<KeyValuePair<Rectangle, string>>();   // кнопки: прямоугольник → действие
        string hover, pressed;

        public event Action OpenMain, HideMe, OpenSettings, RefreshNet;
        public event Action<int, int> Moved;
        public event Action<object, bool> RowAction;   // карточка, true — остановить

        int S(int v) { return (int)Math.Round(v * dpi * k); }
        Font F(float pt) { return Fonts.Ui(pt * k); }
        Font FS(float pt) { return Fonts.Semi(pt * k); }
        Font FI(float pt) { return Fonts.Icons(pt * k); }

        public OverlayForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Text = "Старт-пак — оверлей";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            logo = IconLoader.App(S(16) * 2);
            Size = new Size(S(300), S(60));
            moveTimer.Tick += delegate { moveTimer.Stop(); if (Moved != null) Moved(Left, Top); };
            LocationChanged += delegate { moveTimer.Stop(); moveTimer.Start(); };
            ContextMenuStrip = BuildMenu();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */ | 0x80000 /* LAYERED */;
                if (clickThrough) cp.ExStyle |= 0x20; /* TRANSPARENT — клики проходят насквозь */
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr h, int i, int v);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }   // скруглённые углы (Windows 11)
        }

        public void Apply(Config c)
        {
            Opacity = Math.Max(0.2, Math.Min(1.0, c.OverlayOpacity / 100.0));
            float nk = Math.Max(0.7f, Math.Min(1.6f, c.OverlayScale / 100f));
            if (Math.Abs(nk - k) > 0.001f)
            {
                k = nk;
                if (logo != null) logo.Dispose();
                logo = IconLoader.App(S(16) * 2);
            }
            showStatus = c.OverlayStatus;
            showMs = c.OverlayMs;
            buttons = c.OverlayButtons;
            if (c.OverlayClickThrough != clickThrough)
            {
                clickThrough = c.OverlayClickThrough;
                if (IsHandleCreated)
                {
                    int ex = GetWindowLong(Handle, -20);
                    ex = clickThrough ? ex | 0x20 : ex & ~0x20;
                    SetWindowLong(Handle, -20, ex);
                }
            }
            Relayout();
        }

        public void SetData(List<OverlayRow> r, List<NetTarget> n, bool checking)
        {
            rows = r;
            net = n ?? new List<NetTarget>();
            netBusy = checking;
            Relayout();
        }

        string NetLabel(NetTarget n)
        {
            if (showMs && n.State == "ok" && n.Ms >= 0) return n.Name + " " + n.Ms;
            return n.Name;
        }

        void Relayout()
        {
            int w = S(300);
            if (net.Count > 0)
            {
                int nw = S(12) * 2 + (buttons ? S(26) : 0);
                foreach (NetTarget n in net) nw += TextRenderer.MeasureText(NetLabel(n), F(8f)).Width + S(20);
                w = Math.Max(w, nw);
            }
            int h = S(36) + Math.Max(1, rows.Count) * S(24) + (net.Count > 0 ? S(28) : 0) + S(8);
            if (Size != new Size(w, h)) Size = new Size(w, h);
            Invalidate();
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = Menus.New();
            Menus.Add(m.Items, "Открыть Старт-пак", null, delegate { if (OpenMain != null) OpenMain(); });
            Menus.Add(m.Items, "Проверить связь", null, delegate { if (RefreshNet != null) RefreshNet(); });
            Menus.Add(m.Items, "Настроить оверлей", null, delegate { if (OpenSettings != null) OpenSettings(); });
            Menus.Add(m.Items, "Скрыть оверлей  (Ctrl+Alt+O)", null, delegate { if (HideMe != null) HideMe(); });
            return m;
        }

        string HotAt(Point p)
        {
            foreach (var h in hot) if (h.Key.Contains(p)) return h.Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HotAt(e.Location);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = e.Button == MouseButtons.Left ? HotAt(e.Location) : null;
            if (pressed != null) return;
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1 /* NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string h = HotAt(e.Location);
            string p = pressed;
            pressed = null;
            if (h == null || h != p) return;
            if (h == "net") { if (RefreshNet != null) RefreshNet(); return; }
            int i;
            if (h.StartsWith("row:") && int.TryParse(h.Substring(4), out i) && i < rows.Count && RowAction != null)
                RowAction(rows[i].Tag, rows[i].Running);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (HotAt(e.Location) == null && OpenMain != null) OpenMain();
        }

        void Btn(Graphics g, Rectangle r, string id, string glyph, Color hoverColor)
        {
            Theme t = Theme.Current;
            bool hv = hover == id;
            if (hv) Gfx.Fill(g, r, S(6), t.SurfaceAlt);
            hot.Add(new KeyValuePair<Rectangle, string>(r, id));
            if (glyph == Glyph.Stop) { Gfx.StopMark(g, r, hv ? hoverColor : t.TextDim, S(9)); return; }
            Gfx.Text(g, glyph, FI(8.5f), r, hv ? hoverColor : t.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(t.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var pen = new Pen(t.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            hot.Clear();
            bool btns = buttons && !clickThrough;

            int x = S(12), y = S(9);
            if (logo != null) g.DrawImage(logo, x, y, S(18), S(18));
            Gfx.Text(g, "Старт-пак", FS(9.5f), new Rectangle(x + S(24), y - S(1), S(140), S(20)), t.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            int up = rows.Count(r => r.Dot == t.Green);
            Gfx.Text(g, up + " из " + rows.Count + " работают", F(8.5f), new Rectangle(Width - S(150), y - S(1), S(138), S(20)), t.TextDim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            y = S(36);
            if (rows.Count == 0)
                Gfx.Text(g, "здесь пока пусто", F(9f), new Rectangle(x, y, Width - 2 * x, S(22)), t.TextDim, TextFormatFlags.VerticalCenter);
            for (int i = 0; i < rows.Count; i++)
            {
                OverlayRow r = rows[i];
                int right = Width - S(12);
                if (btns && r.CanAct)
                {
                    var br = new Rectangle(right - S(22), y, S(22), S(22));
                    if (r.Running) Btn(g, br, "row:" + i, Glyph.Stop, t.Red);
                    else Btn(g, br, "row:" + i, Glyph.Play, t.Accent);
                }
                if (btns) right -= S(28);
                int d = S(7);
                using (var b = new SolidBrush(r.Dot)) g.FillEllipse(b, x, y + (S(22) - d) / 2, d, d);
                int sw = 0;
                if (showStatus)
                {
                    sw = Math.Min(TextRenderer.MeasureText(r.Status ?? "", F(8.5f)).Width, S(130));
                    Gfx.Text(g, r.Status, F(8.5f), new Rectangle(right - sw, y, sw, S(22)), r.Dot == t.Red ? t.Red : t.TextDim,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
                Gfx.Text(g, r.Name, F(9f), new Rectangle(x + S(14), y, right - sw - S(10) - x - S(14), S(22)), t.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                y += S(24);
            }
            if (net.Count > 0)
            {
                y += S(2);
                using (var pen = new Pen(t.Border)) g.DrawLine(pen, x, y, Width - x, y);
                int nx = x;
                foreach (NetTarget n in net)
                {
                    Color c = n.State == "ok" ? t.Green : n.State == "fail" ? t.Red : n.State == "checking" ? t.Blue : t.TextFaint;
                    int d = S(6);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, nx, y + S(11), d, d);
                    string label = NetLabel(n);
                    int tw = TextRenderer.MeasureText(label, F(8f)).Width;
                    Gfx.Text(g, label, F(8f), new Rectangle(nx + S(9), y + S(4), tw + 2, S(20)), n.State == "fail" ? t.Red : t.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    nx += tw + S(20);
                }
                if (btns)
                {
                    var rr = new Rectangle(Width - S(12) - S(22), y + S(3), S(22), S(22));
                    if (netBusy) Gfx.Text(g, "…", F(9f), rr, t.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    else Btn(g, rr, "net", Glyph.Refresh, t.Accent);
                }
            }
        }
    }

    // Ползунок в цветах темы.
    public class Slider : Control
    {
        int min = 0, max = 100, val = 50;
        bool drag, hover;
        float sc = 1f;
        public event EventHandler ValueChanged;

        public Slider(int min, int max, int value)
        {
            this.min = min; this.max = max; val = Math.Max(min, Math.Min(max, value));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Size = new Size((int)(260 * sc), (int)(28 * sc));
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        float Knob { get { return 9 * sc; } }

        void SetFrom(int px)
        {
            float w = Width - 2 * Knob;
            Value = min + (int)Math.Round((max - min) * Math.Max(0, Math.Min(1, (px - Knob) / w)));
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); drag = true; SetFrom(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { Value -= 5; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value += 5; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Knob, cy = Height / 2f, w = Width - 2 * k;
            float pos = k + w * (val - min) / (float)Math.Max(1, max - min);
            Gfx.Fill(g, new RectangleF(k, cy - 2 * sc, w, 4 * sc), 2 * sc, t.Track);
            Gfx.Fill(g, new RectangleF(k, cy - 2 * sc, pos - k, 4 * sc), 2 * sc, t.Accent);
            using (var b = new SolidBrush(hover || drag ? t.AccentHover : t.Accent)) g.FillEllipse(b, pos - k, cy - k, 2 * k, 2 * k);
            using (var b = new SolidBrush(t.OnAccent)) g.FillEllipse(b, pos - k / 2.6f, cy - k / 2.6f, 2 * k / 2.6f, 2 * k / 2.6f);
            if (Focused && ShowFocusCues) using (var pen = new Pen(t.Blue, 1.5f * sc)) g.DrawEllipse(pen, pos - k - 2, cy - k - 2, 2 * k + 4, 2 * k + 4);
        }
    }
}
