// Список программ карточками: переключатель, значок, название, состояние, кнопки. Перетаскивание меняет порядок.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StartPack
{
    public class CardInfo
    {
        public Color Dot;
        public string Status = "";
        public string Detail = "";
        public string Badge;                 // «↓ 1.10.4» — есть обновление
        public List<KeyValuePair<string, string>> Chips = new List<KeyValuePair<string, string>>();  // id → текст
        public bool Off;
        public int Progress = -1;
        public string Tip;
        public bool Running;                 // работает — вместо ▶ кнопка «Остановить»
    }

    public class CardList : Control
    {
        public List<Item> Items = new List<Item>();
        public Func<Item, CardInfo> Describe;
        public Func<Item, Image> IconOf;
        public string EmptyText = "";

        public event Action<Item> ToggleClick, RunClick, StopClick, BadgeClick, Activate;
        public event Action<Item, Point> MenuClick;
        public event Action<Item, string> ChipClick;
        public event Action<int, int> Reorder;

        float sc = 1f;
        int scroll;
        Item hoverItem; string hoverPart;
        Item downItem; string downPart; Point downPos;
        bool dragging; int dragFrom = -1, dragTo = -1;
        readonly ToolTip tip = new ToolTip { InitialDelay = 500, ReshowDelay = 200 };
        string tipShown;

        int S(int v) { return (int)Math.Round(v * sc); }
        int CardH { get { return S(74); } }
        int Gap { get { return S(10); } }

        public CardList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
        }

        int ContentHeight { get { return Items.Count == 0 ? 0 : Items.Count * (CardH + Gap) - Gap + S(4); } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }
        bool HasBar { get { return ContentHeight > Height; } }

        public void ScrollTo(int i)
        {
            if (i < 0) return;
            int top = i * (CardH + Gap), bottom = top + CardH;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + Height) scroll = bottom - Height + S(4);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            Invalidate();
        }

        // ---------- разметка карточки

        class Geo
        {
            public Rectangle Card, Toggle, Icon, Run, Menu, Badge, Text;
            public List<KeyValuePair<string, Rectangle>> Chips = new List<KeyValuePair<string, Rectangle>>();
        }

        Geo GeoAt(int i, CardInfo ci)
        {
            var g = new Geo();
            int w = Width - (HasBar ? S(12) : 0);
            g.Card = new Rectangle(0, i * (CardH + Gap) - scroll, w - 1, CardH);
            int cy = g.Card.Y + CardH / 2;
            g.Toggle = new Rectangle(g.Card.X + S(18), cy - S(11), S(40), S(22));
            g.Icon = new Rectangle(g.Toggle.Right + S(16), cy - S(17), S(34), S(34));
            int x = g.Card.Right - S(12);
            g.Menu = new Rectangle(x - S(34), cy - S(17), S(34), S(34)); x = g.Menu.X - S(4);
            g.Run = new Rectangle(x - S(34), cy - S(17), S(34), S(34)); x = g.Run.X - S(10);
            Font cf = Fonts.Semi(9.5f);
            for (int k = ci.Chips.Count - 1; k >= 0; k--)
            {
                int cw = TextRenderer.MeasureText(ci.Chips[k].Value, cf).Width + S(22);
                var r = new Rectangle(x - cw, cy - S(15), cw, S(30));
                g.Chips.Insert(0, new KeyValuePair<string, Rectangle>(ci.Chips[k].Key, r));
                x = r.X - S(8);
            }
            if (!string.IsNullOrEmpty(ci.Badge))
            {
                int bw = TextRenderer.MeasureText(ci.Badge, cf).Width + S(22);
                g.Badge = new Rectangle(x - bw, cy - S(14), bw, S(28));
                x = g.Badge.X - S(10);
            }
            g.Text = new Rectangle(g.Icon.Right + S(14), g.Card.Y, Math.Max(S(40), x - g.Icon.Right - S(18)), CardH);
            return g;
        }

        // ---------- рисование

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (Items.Count == 0)
            {
                Gfx.Text(g, EmptyText, Fonts.Ui(10.5f), new Rectangle(S(20), 0, Width - S(40), Math.Min(Height, S(260))), t.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                CardInfo ci = Describe != null ? Describe(it) : new CardInfo();
                Geo geo = GeoAt(i, ci);
                if (geo.Card.Bottom < 0 || geo.Card.Top > Height) continue;
                bool hov = hoverItem == it && !dragging;
                bool isDragged = dragging && dragFrom == i;

                Color cardBg = hov ? t.SurfaceHover : t.Surface;
                Gfx.Fill(g, geo.Card, S(12), cardBg);
                Gfx.Stroke(g, new RectangleF(geo.Card.X + 0.5f, geo.Card.Y + 0.5f, geo.Card.Width - 1, geo.Card.Height - 1), S(12),
                    isDragged ? t.Accent : t.Border, isDragged ? 1.5f * sc : 1f);

                // переключатель
                Color tc = !ci.Off ? (hoverPart == "toggle" && hov ? t.AccentHover : t.Accent) : (hoverPart == "toggle" && hov ? Gfx.Mix(t.Track, t.Text, 0.15f) : t.Track);
                Gfx.Fill(g, geo.Toggle, geo.Toggle.Height / 2f, tc);
                float k = geo.Toggle.Height - 6 * sc;
                float kx = !ci.Off ? geo.Toggle.Right - k - 3 * sc : geo.Toggle.X + 3 * sc;
                using (var b = new SolidBrush(!ci.Off ? t.OnAccent : Color.White)) g.FillEllipse(b, kx, geo.Toggle.Y + 3 * sc, k, k);

                // значок
                Image img = IconOf != null ? IconOf(it) : null;
                if (img != null)
                {
                    if (ci.Off)
                    {
                        using (var ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f };
                            ia.SetColorMatrix(cm);
                            g.DrawImage(img, geo.Icon, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    else g.DrawImage(img, geo.Icon);
                }

                // текст
                Color nameC = ci.Off ? t.TextFaint : t.Text;
                Font nf = Fonts.Semi(11f), sf = Fonts.Ui(9.5f);
                int nameH = TextRenderer.MeasureText("Ag", nf).Height, subH = TextRenderer.MeasureText("Ag", sf).Height;
                int top = geo.Card.Y + (CardH - nameH - subH - S(2)) / 2;
                Gfx.Text(g, it.Name, nf, new Rectangle(geo.Text.X, top, geo.Text.Width, nameH), nameC,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int ly = top + nameH + S(2);
                int dot = S(8);
                using (var b = new SolidBrush(ci.Off ? t.TextFaint : ci.Dot)) g.FillEllipse(b, geo.Text.X, ly + (subH - dot) / 2, dot, dot);
                int sx = geo.Text.X + dot + S(7);
                string status = ci.Status ?? "";
                int stW = TextRenderer.MeasureText(status, sf, Size.Empty, TextFormatFlags.NoPadding).Width;
                stW = Math.Min(stW, geo.Text.Right - sx);
                Gfx.Text(g, status, sf, new Rectangle(sx, ly, stW + 1, subH), ci.Off ? t.TextFaint : (ci.Dot == t.Red ? t.Red : t.Text),
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (!string.IsNullOrEmpty(ci.Detail) && sx + stW + S(20) < geo.Text.Right)
                    Gfx.Text(g, "   ·   " + ci.Detail, sf, new Rectangle(sx + stW, ly, geo.Text.Right - sx - stW, subH), t.TextDim,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                // значок обновления
                if (!string.IsNullOrEmpty(ci.Badge))
                {
                    bool bh = hov && hoverPart == "badge";
                    Gfx.Fill(g, geo.Badge, geo.Badge.Height / 2f, bh ? t.BlueHover : t.Blue);
                    Gfx.Text(g, ci.Badge, Fonts.Semi(9.5f), geo.Badge, t.OnBlue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                // кнопки-действия (подключить, починить hosts…)
                foreach (var ch in geo.Chips)
                {
                    bool chh = hov && hoverPart == "chip:" + ch.Key;
                    string label = ci.Chips.First(c => c.Key == ch.Key).Value;
                    Gfx.Fill(g, ch.Value, S(8), chh ? Gfx.Mix(t.SurfaceAlt, t.Text, 0.08f) : t.SurfaceAlt);
                    Gfx.Text(g, label, Fonts.Semi(9.5f), ch.Value, t.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                // ▶ и ⋯
                if (ci.Running) DrawIconBtn(g, geo.Run, Glyph.Stop, hov && hoverPart == "run", t.Red);
                else DrawIconBtn(g, geo.Run, Glyph.Play, hov && hoverPart == "run", t.Accent);
                DrawIconBtn(g, geo.Menu, Glyph.More, hov && hoverPart == "menu", t.Text);

                // полоска загрузки
                if (ci.Progress >= 0)
                {
                    var track = new RectangleF(geo.Text.X, geo.Card.Bottom - S(9), geo.Card.Right - S(16) - geo.Text.X, S(3));
                    Gfx.Fill(g, track, S(2), t.SurfaceAlt);
                    Gfx.Fill(g, new RectangleF(track.X, track.Y, track.Width * Math.Min(100, ci.Progress) / 100f, track.Height), S(2), t.Accent);
                }
            }

            // куда упадёт перетаскиваемая карточка
            if (dragging && dragTo >= 0 && dragTo != dragFrom && dragTo != dragFrom + 1)
            {
                int y = dragTo * (CardH + Gap) - scroll - Gap / 2;
                Gfx.Fill(g, new RectangleF(S(8), y - S(2), Width - S(28), S(4)), S(2), t.Accent);
            }

            // тонкая полоса прокрутки
            if (HasBar)
            {
                float ratio = Height / (float)ContentHeight;
                float h = Math.Max(S(40), Height * ratio);
                float y = (Height - h) * (scroll / (float)Math.Max(1, MaxScroll));
                Gfx.Fill(g, new RectangleF(Width - S(6), y, S(4), h), S(2), t.SurfaceAlt);
            }
        }

        void DrawIconBtn(Graphics g, Rectangle r, string glyph, bool hover, Color hoverColor)
        {
            Theme t = Theme.Current;
            if (hover) Gfx.Fill(g, r, S(8), t.SurfaceAlt);
            if (glyph == Glyph.Stop) { Gfx.StopMark(g, r, hover ? hoverColor : t.TextDim, S(11)); return; }
            Gfx.Text(g, glyph, Fonts.Icons(11f), r, hover ? hoverColor : t.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ---------- мышь

        void HitTest(Point p, out Item item, out string part, out int index)
        {
            item = null; part = null; index = -1;
            for (int i = 0; i < Items.Count; i++)
            {
                CardInfo ci = Describe != null ? Describe(Items[i]) : new CardInfo();
                Geo geo = GeoAt(i, ci);
                if (!geo.Card.Contains(p)) continue;
                item = Items[i]; index = i;
                if (Rectangle.Inflate(geo.Toggle, S(6), S(10)).Contains(p)) part = "toggle";
                else if (geo.Run.Contains(p)) part = "run";
                else if (geo.Menu.Contains(p)) part = "menu";
                else if (!geo.Badge.IsEmpty && geo.Badge.Contains(p)) part = "badge";
                else
                {
                    foreach (var ch in geo.Chips) if (ch.Value.Contains(p)) { part = "chip:" + ch.Key; break; }
                    if (part == null) part = "body";
                }
                return;
            }
        }

        string TipFor(Item it, string part)
        {
            if (it == null) return null;
            switch (part)
            {
                case "toggle": return it.Enabled ? "Участвует в «Запустить всё» — нажмите, чтобы исключить" : "Не участвует в «Запустить всё» — нажмите, чтобы включить";
                case "run":
                    if (Describe == null || !Describe(it).Running) return "Запустить только эту";
                    return VpnCtl.IsVpn(it) ? "Закрыть полностью (сначала отключит VPN)" : "Остановить";
                case "menu": return "Ещё: изменить, обновить, порядок…";
                case "badge": return "Обновить до новой версии";
                case "body":
                    CardInfo ci = Describe != null ? Describe(it) : null;
                    return ci != null && ci.Tip != null ? ci.Tip : it.Path;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (downItem != null && downPart == "body" && e.Button == MouseButtons.Left && !dragging &&
                (Math.Abs(e.X - downPos.X) > S(6) || Math.Abs(e.Y - downPos.Y) > S(6)))
            {
                dragging = true;
                dragFrom = Items.IndexOf(downItem);
                Cursor = Cursors.SizeNS;
                tip.Hide(this);
            }
            if (dragging)
            {
                int slot = (e.Y + scroll + (CardH + Gap) / 2) / (CardH + Gap);
                dragTo = Math.Max(0, Math.Min(Items.Count, slot));
                if (e.Y < S(20)) { scroll = Math.Max(0, scroll - S(12)); }
                else if (e.Y > Height - S(20)) { scroll = Math.Min(MaxScroll, scroll + S(12)); }
                Invalidate();
                return;
            }
            Item it; string part; int idx;
            HitTest(e.Location, out it, out part, out idx);
            if (it != hoverItem || part != hoverPart)
            {
                hoverItem = it; hoverPart = part;
                Cursor = part != null && part != "body" ? Cursors.Hand : Cursors.Default;
                string tt = TipFor(it, part);
                if (tt != tipShown) { tipShown = tt; tip.SetToolTip(this, tt ?? ""); }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging) { hoverItem = null; hoverPart = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int idx;
            HitTest(e.Location, out downItem, out downPart, out idx);
            downPos = e.Location;
            if (e.Button == MouseButtons.Right && downItem != null)
            {
                if (MenuClick != null) MenuClick(downItem, e.Location);
                downItem = null;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                int from = dragFrom, to = dragTo;
                dragging = false; dragFrom = -1; dragTo = -1;
                Cursor = Cursors.Default;
                downItem = null;
                if (from >= 0 && to >= 0 && to != from && to != from + 1 && Reorder != null)
                    Reorder(from, to > from ? to - 1 : to);
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left || downItem == null) { downItem = null; return; }
            Item it; string part; int idx;
            HitTest(e.Location, out it, out part, out idx);
            Item d = downItem; string dp = downPart;
            downItem = null;
            if (it != d || part != dp) return;
            if (part == "toggle" && ToggleClick != null) ToggleClick(it);
            else if (part == "run")
            {
                bool running = Describe != null && Describe(it).Running;
                if (running) { if (StopClick != null) StopClick(it); }
                else if (RunClick != null) RunClick(it);
            }
            else if (part == "menu" && MenuClick != null) MenuClick(it, new Point(e.X, e.Y));
            else if (part == "badge" && BadgeClick != null) BadgeClick(it);
            else if (part != null && part.StartsWith("chip:") && ChipClick != null) ChipClick(it, part.Substring(5));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Item it; string part; int idx;
            HitTest(e.Location, out it, out part, out idx);
            if (it != null && part == "body" && Activate != null) Activate(it);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(e.Delta) * S(64)));
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
        }
    }

    // ---------------------------------------------------------------- простой список для выбора (Пуск, процессы)

    public class PickRow
    {
        public string Title, Sub, Value;
        public Image Image;
        public object Tag;
    }

    public class RowList : Control
    {
        List<PickRow> rows = new List<PickRow>();
        public int Selected = -1;
        int hover = -1, scroll;
        float sc = 1f;
        public event EventHandler Activate;

        int S(int v) { return (int)Math.Round(v * sc); }
        int RowH { get { return S(52); } }

        public RowList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            TabStop = true;
        }

        public string Empty = "Ничего не найдено";

        public void SetRows(List<PickRow> r)
        {
            rows = r;
            Selected = rows.Count > 0 ? 0 : -1;
            scroll = 0;
            Invalidate();
        }

        public PickRow Current { get { return Selected >= 0 && Selected < rows.Count ? rows[Selected] : null; } }

        int MaxScroll { get { return Math.Max(0, rows.Count * RowH - Height); } }

        void Ensure(int i)
        {
            if (i < 0) return;
            if (i * RowH < scroll) scroll = i * RowH;
            else if ((i + 1) * RowH > scroll + Height) scroll = (i + 1) * RowH - Height;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(t.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (rows.Count == 0)
            {
                Gfx.Text(g, Empty, Fonts.Ui(10f), ClientRectangle, t.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int first = scroll / RowH;
            for (int i = first; i < rows.Count && i * RowH - scroll < Height; i++)
            {
                var r = new Rectangle(S(6), i * RowH - scroll + S(2), Width - S(14), RowH - S(4));
                if (i == Selected) Gfx.Fill(g, r, S(8), t.SurfaceAlt);
                else if (i == hover) Gfx.Fill(g, r, S(8), t.SurfaceHover);
                PickRow row = rows[i];
                var ir = new Rectangle(r.X + S(10), r.Y + (r.Height - S(28)) / 2, S(28), S(28));
                if (row.Image != null) g.DrawImage(row.Image, ir);
                int tx = ir.Right + S(12);
                bool hasSub = !string.IsNullOrEmpty(row.Sub);
                int th = TextRenderer.MeasureText("Ag", Fonts.Ui(10f)).Height;
                int sh = TextRenderer.MeasureText("Ag", Fonts.Ui(8.5f)).Height;
                int ty = hasSub ? r.Y + (r.Height - th - sh) / 2 : r.Y + (r.Height - th) / 2;
                Gfx.Text(g, row.Title, Fonts.Ui(10f), new Rectangle(tx, ty, r.Right - tx - S(8), th), t.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (hasSub)
                    Gfx.Text(g, row.Sub, Fonts.Ui(8.5f), new Rectangle(tx, ty + th, r.Right - tx - S(8), sh), t.TextDim, TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            if (rows.Count * RowH > Height)
            {
                float h = Math.Max(S(30), Height * Height / (float)(rows.Count * RowH));
                float y = (Height - h) * (scroll / (float)Math.Max(1, MaxScroll));
                Gfx.Fill(g, new RectangleF(Width - S(6), y, S(4), h), S(2), t.SurfaceAlt);
            }
        }

        int At(Point p) { int i = (p.Y + scroll) / RowH; return i >= 0 && i < rows.Count ? i : -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); int i = At(e.Location); if (i >= 0) { Selected = i; Invalidate(); } }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (At(e.Location) >= 0 && Activate != null) Activate(this, EventArgs.Empty); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(e.Delta) * RowH * 2)); Invalidate(); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Enter || base.IsInputKey(k); }

        public void Step(int d)
        {
            if (rows.Count == 0) return;
            Selected = Math.Max(0, Math.Min(rows.Count - 1, Selected + d));
            Ensure(Selected);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { Step(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && Activate != null) { Activate(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }
}
