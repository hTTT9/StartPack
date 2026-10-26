// Тема (тёмная / светлая / как в Windows) и свои элементы интерфейса: кнопки, переключатели, поля, меню, диалоги.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StartPack
{
    public class Theme
    {
        public bool Dark;
        public Color Bg, Surface, SurfaceHover, SurfaceAlt, Border, Text, TextDim, TextFaint;
        public Color Accent, AccentHover, OnAccent, Blue, BlueHover, OnBlue, Green, Red, Amber, Track;

        public static Theme Current = MakeDark();
        public static string Mode = "dark";

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static Theme MakeDark()
        {
            return new Theme
            {
                Dark = true,
                Bg = C(0x111318), Surface = C(0x1A1D24), SurfaceHover = C(0x21252E), SurfaceAlt = C(0x2A2F3A),
                Border = C(0x262A33), Text = C(0xE8EAED), TextDim = C(0x9AA3AF), TextFaint = C(0x6B7280),
                Accent = C(0x34D399), AccentHover = C(0x6EE7B7), OnAccent = C(0x052E1C),
                Blue = C(0x60A5FA), BlueHover = C(0x93C5FD), OnBlue = C(0x0B1B33),
                Green = C(0x34D399), Red = C(0xF87171), Amber = C(0xFBBF24), Track = C(0x3A404C)
            };
        }

        public static Theme MakeLight()
        {
            return new Theme
            {
                Dark = false,
                Bg = C(0xF3F4F6), Surface = C(0xFFFFFF), SurfaceHover = C(0xF7F8FA), SurfaceAlt = C(0xE9ECF0),
                Border = C(0xE2E5EA), Text = C(0x1F2328), TextDim = C(0x596273), TextFaint = C(0x8A93A0),
                Accent = C(0x15803D), AccentHover = C(0x166534), OnAccent = C(0xFFFFFF),
                Blue = C(0x2563EB), BlueHover = C(0x1D4ED8), OnBlue = C(0xFFFFFF),
                Green = C(0x15803D), Red = C(0xB91C1C), Amber = C(0xB45309), Track = C(0xC9CED6)
            };
        }

        public static void Apply(string mode)
        {
            Mode = mode == "light" || mode == "system" ? mode : "dark";
            bool dark = Mode == "light" ? false : Mode == "system" ? SystemIsDark() : true;
            Current = dark ? MakeDark() : MakeLight();
        }

        static bool SystemIsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return true; }
        }
    }

    public static class Fonts
    {
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        public static Font Ui(float size) { return Get("Segoe UI", size, FontStyle.Regular); }
        public static Font Semi(float size) { return Get("Segoe UI Semibold", size, FontStyle.Regular); }
        public static Font Bold(float size) { return Get("Segoe UI", size, FontStyle.Bold); }
        public static Font Icons(float size) { return Get(IconFamily, size, FontStyle.Regular); }

        static string iconFamily;
        static string IconFamily
        {
            get
            {
                if (iconFamily == null)
                {
                    iconFamily = "Segoe MDL2 Assets";
                    using (var fc = new InstalledFontCollection())
                        foreach (FontFamily f in fc.Families)
                            if (f.Name == "Segoe Fluent Icons") { iconFamily = f.Name; break; }
                }
                return iconFamily;
            }
        }

        static Font Get(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + style;
            Font f;
            if (!cache.TryGetValue(key, out f)) { f = new Font(family, size, style); cache[key] = f; }
            return f;
        }
    }

    // Символы из шрифта значков Windows (Segoe Fluent Icons / MDL2).
    public static class Glyph
    {
        public const string Play = "", More = "", Settings = "", Add = "", Download = "",
            Refresh = "", Folder = "", Edit = "", Delete = "", Up = "", Down = "",
            Link = "", Unlink = "", Shield = "", Check = "", Search = "", App = "",
            Store = "", Process = "", File = "", Keyboard = "", Star = "", Sync = "",
            Stop = "", Power = "", Export = "", Import = "", Reset = "", Web = "", Eye = "",
            Globe = "", Save = "", Heart = "";

        public static Bitmap Image(string g, Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics gr = Graphics.FromImage(bmp))
            {
                if (g == Stop) { gr.SmoothingMode = SmoothingMode.AntiAlias; Gfx.StopMark(gr, new RectangleF(0, 0, size, size), color, size * 0.42f); return bmp; }
                gr.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var f = new Font(Fonts.Icons(10).FontFamily, size * 0.62f, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    gr.DrawString(g, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }

    public static class Gfx
    {
        // Значок «стоп»: сплошной скруглённый квадрат (контурный из шрифта похож на флажок).
        public static void StopMark(Graphics g, RectangleF r, Color c, float size)
        {
            var q = new RectangleF(r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f, size, size);
            Fill(g, q, size * 0.22f, c);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (GraphicsPath p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static float Scale(Control c)
        {
            using (Graphics g = c.CreateGraphics()) return g.DpiX / 96f;
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix);
        }
    }

    public static class Win32
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string id);

        public static void StyleTitle(Form f)
        {
            try
            {
                int dark = Theme.Current.Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);              // тёмная рамка (Windows 10 2004+)
                int cap = ColorTranslator.ToWin32(Theme.Current.Bg);
                DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);               // цвет заголовка (Windows 11)
                int txt = ColorTranslator.ToWin32(Theme.Current.Text);
                DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
                int border = ColorTranslator.ToWin32(Theme.Current.Border);
                DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
            }
            catch { }
        }

        public static void Scrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, Theme.Current.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    // ---------------------------------------------------------------- кнопка

    public enum BtnKind { Primary, Secondary, Ghost, Blue, Danger }

    public class FlatBtn : Control, IButtonControl
    {
        public BtnKind Kind;
        public string Icon;
        public bool Compact;
        bool hover, down;
        float sc = 1f;

        public DialogResult DialogResult { get; set; }

        public FlatBtn(string text, BtnKind kind, string icon)
        {
            Text = text; Kind = kind; Icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Fonts.Semi(10f);
            TabStop = true;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); sc = Gfx.Scale(this); }

        public void Fit()
        {
            if (IsHandleCreated) sc = Gfx.Scale(this); else using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Size = GetPreferredSize(Size.Empty);
        }

        public override Size GetPreferredSize(Size proposed)
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font);
            int iconW = string.IsNullOrEmpty(Icon) ? 0 : (int)(16 * sc) + (string.IsNullOrEmpty(Text) ? 0 : (int)(8 * sc));
            int padX = (int)((Compact ? 10 : 16) * sc);
            int w = (string.IsNullOrEmpty(Text) ? 0 : t.Width) + iconW + padX * 2;
            int h = (int)((Compact ? 32 : 38) * sc);
            if (string.IsNullOrEmpty(Text)) w = h;
            return new Size(w, h);
        }

        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None)
            {
                Form f = FindForm();
                if (f != null) f.DialogResult = DialogResult;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { PerformClick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            Color parentBg = Parent != null ? Parent.BackColor : t.Bg;
            g.Clear(parentBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg, fg;
            switch (Kind)
            {
                case BtnKind.Primary: bg = hover ? t.AccentHover : t.Accent; fg = t.OnAccent; break;
                case BtnKind.Blue: bg = hover ? t.BlueHover : t.Blue; fg = t.OnBlue; break;
                case BtnKind.Secondary: bg = hover ? Gfx.Mix(t.SurfaceAlt, t.Text, 0.08f) : t.SurfaceAlt; fg = t.Text; break;
                case BtnKind.Danger: bg = hover ? t.SurfaceAlt : parentBg; fg = t.Red; break;
                default: bg = hover ? t.SurfaceAlt : parentBg; fg = hover ? t.Text : t.TextDim; break;
            }
            if (down) bg = Gfx.Mix(bg, parentBg, 0.25f);
            if (!Enabled) { bg = Kind == BtnKind.Ghost || Kind == BtnKind.Danger ? parentBg : t.SurfaceAlt; fg = t.TextFaint; }

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Gfx.Fill(g, r, 8 * sc, bg);
            if (Focused && ShowFocusCues) Gfx.Stroke(g, new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f), 7 * sc, t.Blue, 1.5f * sc);

            Size ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, Font);
            int iconW = string.IsNullOrEmpty(Icon) ? 0 : (int)(16 * sc);
            int gap = iconW > 0 && ts.Width > 0 ? (int)(8 * sc) : 0;
            int total = iconW + gap + ts.Width;
            int x = (Width - total) / 2;
            if (iconW > 0)
            {
                Gfx.Text(g, Icon, Fonts.Icons(11f), new Rectangle(x, 0, iconW, Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += iconW + gap;
            }
            if (ts.Width > 0)
                Gfx.Text(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    // ---------------------------------------------------------------- переключатель с подписью

    public class Toggle : Control
    {
        bool isOn, hover;
        public string Hint;
        bool glow;
        public bool Glow { get { return glow; } set { glow = value; Invalidate(); } }   // подсветить, чтобы заметили
        float sc = 1f;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isOn; }
            set { if (isOn == value) return; isOn = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public Toggle(string text, string hint, bool on)
        {
            Text = text; Hint = hint; isOn = on;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Fonts.Ui(10f);
            TabStop = true;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
        }

        int MaxTextW { get { return (int)(470 * sc); } }

        public void Fit() { Size = GetPreferredSize(Size.Empty); }

        public override Size GetPreferredSize(Size proposed)
        {
            int trackW = (int)(40 * sc), gap = (int)(12 * sc);
            Size ts = TextRenderer.MeasureText(Text ?? "", Font, new Size(MaxTextW, 0), TextFormatFlags.WordBreak);
            Size hs = string.IsNullOrEmpty(Hint) ? Size.Empty : TextRenderer.MeasureText(Hint, Fonts.Ui(9f), new Size(MaxTextW, 0), TextFormatFlags.WordBreak);
            int h = Math.Max((int)(24 * sc), ts.Height + (hs.Height > 0 ? hs.Height + (int)(2 * sc) : 0));
            return new Size(trackW + gap + Math.Max(ts.Width, hs.Width) + (int)(4 * sc), h + (int)(4 * sc));
        }

        protected override void OnTextChanged(EventArgs e) { Fit(); Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int trackW = (int)(40 * sc), trackH = (int)(22 * sc), gap = (int)(12 * sc);
            var track = new RectangleF(1, (int)(2 * sc), trackW, trackH);
            Color tc = isOn ? (hover ? t.AccentHover : t.Accent) : (hover ? Gfx.Mix(t.Track, t.Text, 0.15f) : t.Track);
            if (!Enabled) tc = t.SurfaceAlt;
            Gfx.Fill(g, track, trackH / 2f, tc);
            float k = trackH - 6 * sc;
            float kx = isOn ? track.Right - k - 3 * sc : track.X + 3 * sc;
            using (var b = new SolidBrush(isOn ? t.OnAccent : Color.White)) g.FillEllipse(b, kx, track.Y + 3 * sc, k, k);
            if (Focused && ShowFocusCues) Gfx.Stroke(g, RectangleF.Inflate(track, 2, 2), trackH / 2f + 2, t.Blue, 1.5f);
            if (Glow) Gfx.Stroke(g, RectangleF.Inflate(track, 1.5f * sc, 1.5f * sc), trackH / 2f + 2 * sc, t.Amber, 2f * sc);

            int tx = trackW + gap;
            Color fc = Enabled ? t.Text : t.TextFaint;
            Size ts = TextRenderer.MeasureText(Text ?? "", Font, new Size(MaxTextW, 0), TextFormatFlags.WordBreak);
            Gfx.Text(g, Text, Font, new Rectangle(tx, (int)(2 * sc), Width - tx, ts.Height), fc, TextFormatFlags.WordBreak);
            if (!string.IsNullOrEmpty(Hint))
                Gfx.Text(g, Hint, Fonts.Ui(9f), new Rectangle(tx, (int)(4 * sc) + ts.Height, Width - tx, Height), t.TextDim, TextFormatFlags.WordBreak);
        }
    }

    // ---------------------------------------------------------------- поле ввода со скруглённой рамкой

    public class TextField : Panel
    {
        public readonly TextBox Box;
        float sc = 1f;

        public TextField(string text, int width)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, Text = text ?? "", Font = Fonts.Ui(10f) };
            Padding = new Padding((int)(10 * sc), (int)(8 * sc), (int)(10 * sc), (int)(6 * sc));
            Size = new Size(width, Box.PreferredHeight + Padding.Vertical + 2);
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Restyle();
        }

        public override string Text { get { return Box == null ? "" : Box.Text; } set { if (Box != null) Box.Text = value; } }

        public void Restyle()
        {
            BackColor = Theme.Current.Surface;
            Box.BackColor = Theme.Current.Surface;
            Box.ForeColor = Theme.Current.Text;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.ForeColor = Enabled ? Theme.Current.Text : Theme.Current.TextFaint;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Gfx.Fill(g, r, 7 * sc, Enabled ? t.Surface : t.Bg);
            Gfx.Stroke(g, r, 7 * sc, Box.Focused ? t.Accent : t.Border, Box.Focused ? 1.5f * sc : 1f);
        }
    }

    // ---------------------------------------------------------------- выпадающий список в цветах темы

    // Свой выпадающий список: поле со стрелкой, по щелчку — меню в цветах темы.
    public class DarkCombo : Control
    {
        public readonly List<object> Items = new List<object>();
        int selected = -1;
        bool hover, open;
        float sc = 1f;
        public event EventHandler SelectedIndexChanged;

        public DarkCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Font = Fonts.Ui(10f);
            Height = (int)(38 * sc);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = Items.Count == 0 ? -1 : Math.Max(-1, Math.Min(Items.Count - 1, value));
                if (v == selected) return;
                selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public void Restyle() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { SelectedIndex = selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, selected - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Space || (e.KeyCode == Keys.Down && e.Alt)) { ShowList(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) ShowList();
        }

        void ShowList()
        {
            if (Items.Count == 0) return;
            ContextMenuStrip m = Menus.New();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            for (int i = 0; i < Items.Count; i++)
            {
                int k = i;
                var mi = new ToolStripMenuItem(Convert.ToString(Items[i])) { Checked = i == selected, Padding = new Padding(2, 5, 8, 5) };
                mi.Click += delegate { SelectedIndex = k; };
                m.Items.Add(mi);
            }
            m.MinimumSize = new Size(Width, 0);
            m.Closed += delegate { open = false; Invalidate(); };
            open = true;
            Invalidate();
            m.Show(this, new Point(0, Height + (int)(2 * sc)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Gfx.Fill(g, r, 7 * sc, Enabled ? (hover ? t.SurfaceHover : t.Surface) : t.Bg);
            bool focus = Focused || open;
            Gfx.Stroke(g, r, 7 * sc, focus ? t.Accent : t.Border, focus ? 1.5f * sc : 1f);
            int chev = (int)(30 * sc);
            string text = selected >= 0 && selected < Items.Count ? Convert.ToString(Items[selected]) : "";
            Gfx.Text(g, text, Font, new Rectangle((int)(11 * sc), 0, Width - chev - (int)(14 * sc), Height), Enabled ? t.Text : t.TextFaint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            Gfx.Text(g, "", Fonts.Icons(9f), new Rectangle(Width - chev - (int)(4 * sc), 0, chev, Height), t.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ---------------------------------------------------------------- меню в цветах темы

    class MenuColors : ProfessionalColorTable
    {
        Theme T { get { return Theme.Current; } }
        public override Color MenuItemSelected { get { return T.SurfaceAlt; } }
        public override Color MenuItemSelectedGradientBegin { get { return T.SurfaceAlt; } }
        public override Color MenuItemSelectedGradientEnd { get { return T.SurfaceAlt; } }
        public override Color MenuItemPressedGradientBegin { get { return T.SurfaceAlt; } }
        public override Color MenuItemPressedGradientEnd { get { return T.SurfaceAlt; } }
        public override Color MenuItemBorder { get { return T.SurfaceAlt; } }
        public override Color MenuBorder { get { return T.Border; } }
        public override Color ToolStripDropDownBackground { get { return T.Surface; } }
        public override Color ImageMarginGradientBegin { get { return T.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return T.Surface; } }
        public override Color ImageMarginGradientEnd { get { return T.Surface; } }
        public override Color SeparatorDark { get { return T.Border; } }
        public override Color SeparatorLight { get { return T.Border; } }
        public override Color CheckBackground { get { return T.SurfaceAlt; } }
        public override Color CheckSelectedBackground { get { return T.SurfaceAlt; } }
    }

    public class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Theme t = Theme.Current;
            Color c = e.Item.Tag as string == "danger" ? t.Red : t.Text;
            e.TextColor = e.Item.Enabled ? c : t.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Current.TextDim;
            base.OnRenderArrow(e);
        }
    }

    public static class Menus
    {
        public static ContextMenuStrip New()
        {
            var m = new ContextMenuStrip { Renderer = new ThemedRenderer(), Font = Fonts.Ui(10f), ShowImageMargin = true };
            m.ImageScalingSize = new Size(18, 18);
            return m;
        }

        public static ToolStripMenuItem Add(ToolStripItemCollection items, string text, string glyph, EventHandler click)
        {
            var mi = new ToolStripMenuItem(text);
            if (glyph != null) mi.Image = Glyph.Image(glyph, Theme.Current.TextDim, 32);
            if (click != null) mi.Click += click;
            mi.Padding = new Padding(2, 4, 2, 4);
            items.Add(mi);
            return mi;
        }
    }

    // ---------------------------------------------------------------- база для окон

    public class ThemedForm : Form
    {
        protected float sc = 1f;
        protected int S(int v) { return (int)Math.Round(v * sc); }

        public ThemedForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Font = Fonts.Ui(10f);
            BackColor = Theme.Current.Bg;
            ForeColor = Theme.Current.Text;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win32.StyleTitle(this);
        }

        public static Label Lbl(string text, string role)
        {
            var l = new Label { Text = text, AutoSize = true, Tag = role, BackColor = Color.Transparent };
            if (role == "dim") { l.ForeColor = Theme.Current.TextDim; l.Font = Fonts.Ui(9f); }
            else if (role == "section") { l.ForeColor = Theme.Current.TextDim; l.Font = Fonts.Semi(9f); }
            else if (role == "title") { l.ForeColor = Theme.Current.Text; l.Font = Fonts.Semi(13f); }
            else l.ForeColor = Theme.Current.Text;
            return l;
        }

        // Перекрасить всё окно после смены темы.
        public static void Restyle(Control root)
        {
            Theme t = Theme.Current;
            Form f = root as Form;
            if (f != null) { f.BackColor = t.Bg; f.ForeColor = t.Text; if (f.IsHandleCreated) Win32.StyleTitle(f); }
            foreach (Control c in root.Controls) RestyleOne(c);
            root.Invalidate(true);
        }

        static void RestyleOne(Control c)
        {
            Theme t = Theme.Current;
            if (c is TextField) ((TextField)c).Restyle();
            else if (c is DarkCombo) ((DarkCombo)c).Restyle();
            else if (c is NumericUpDown) { c.BackColor = t.Surface; c.ForeColor = t.Text; }
            else if (c is Label)
            {
                string role = c.Tag as string;
                c.ForeColor = role == "dim" || role == "section" ? t.TextDim : t.Text;
            }
            else if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel)
            {
                c.BackColor = (c.Tag as string) == "surface" ? t.Surface : c.Parent != null ? c.Parent.BackColor : t.Bg;
            }
            if (!(c is TextField)) foreach (Control ch in c.Controls) RestyleOne(ch);
            c.Invalidate();
        }
    }

    // ---------------------------------------------------------------- вопрос / сообщение

    public static class Dlg
    {
        public static bool Ask(IWin32Window owner, string title, string text, string yes, string no)
        {
            using (var f = new MsgForm(title, text, yes, no))
                return f.ShowDialog(owner) == DialogResult.Yes;
        }

        public static void Info(IWin32Window owner, string title, string text)
        {
            using (var f = new MsgForm(title, text, "Понятно", null)) f.ShowDialog(owner);
        }
    }

    public class MsgForm : ThemedForm
    {
        public MsgForm(string title, string text, string yes, string no)
        {
            Text = Core.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(S(24), S(20), S(24), S(18)) };
            Controls.Add(t);
            Label head = Lbl(title, "title");
            head.MaximumSize = new Size(S(460), 0);
            head.Margin = new Padding(0, 0, 0, S(8));
            Label body = Lbl(text, "");
            body.MaximumSize = new Size(S(460), 0);
            body.MinimumSize = new Size(S(340), 0);
            body.Margin = new Padding(0, 0, 0, S(18));
            t.Controls.Add(head);
            t.Controls.Add(body);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = Padding.Empty };
            var ok = new FlatBtn(yes, BtnKind.Primary, null) { DialogResult = no == null ? DialogResult.OK : DialogResult.Yes };
            ok.Fit();
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            if (no != null)
            {
                var cancel = new FlatBtn(no, BtnKind.Secondary, null) { DialogResult = DialogResult.No, Margin = new Padding(0, 3, S(8), 3) };
                cancel.Fit();
                buttons.Controls.Add(cancel);
                CancelButton = cancel;
            }
            else CancelButton = ok;
            t.Controls.Add(buttons);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Owner == null) CenterToScreen();
            if (AcceptButton is Control) ((Control)AcceptButton).Focus();
        }
    }
}
