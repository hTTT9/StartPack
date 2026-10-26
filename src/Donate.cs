// «Поблагодарить автора»: адрес кошелька, кнопка «Скопировать» и QR-код. Ненавязчиво — открывается только по ссылке.
// Адреса лежат в StartPack.json (раздел Donate) — их можно поменять без пересборки.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StartPack
{
    public class DonateAddr
    {
        public string Title { get; set; }     // что можно прислать
        public string Network { get; set; }   // сеть
        public string Address { get; set; }
        public string Note { get; set; }      // предупреждение для отправителя

        public const string DefaultUrl = "https://www.donationalerts.com/c/hornetdx";

        public static List<DonateAddr> Defaults()
        {
            return new List<DonateAddr>
            {
                new DonateAddr
                {
                    Title = "USDT или TON", Network = "сеть TON",
                    Address = "UQBU1nh9fN8yESuYablLwO9KNOx5rOQe69LgaofPEIFAZIkK",
                    Note = "Отправляйте только в сети TON. USDT из сетей TRON (TRC20), BSC или Ethereum на этот адрес не дойдёт."
                }
            };
        }
    }

    public static class TonAddress
    {
        // Проверка контрольной суммы адреса TON (формат UQ…/EQ…, 48 символов).
        public static bool LooksValid(string a)
        {
            try
            {
                if (a == null || a.Length != 48) return false;
                byte[] raw = Convert.FromBase64String(a.Replace('-', '+').Replace('_', '/'));
                if (raw.Length != 36) return false;
                int reg = 0;
                for (int i = 0; i < 34; i++)
                {
                    reg ^= raw[i] << 8;
                    for (int k = 0; k < 8; k++) reg = ((reg & 0x8000) != 0 ? (reg << 1) ^ 0x1021 : reg << 1) & 0xFFFF;
                }
                return reg == ((raw[34] << 8) | raw[35]);
            }
            catch { return false; }
        }
    }

    public class DonateForm : ThemedForm
    {
        public DonateForm(Config cfg)
        {
            List<DonateAddr> list = cfg.Donate ?? new List<DonateAddr>();
            string url = (cfg.DonateUrl ?? "").Trim();
            Text = "Поблагодарить";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(26), S(20), S(26), S(22)) };
            Controls.Add(t);
            Label head = Lbl("Спасибо, что пользуетесь Старт-паком!", "title");
            head.Margin = new Padding(0, 0, 0, S(6));
            t.Controls.Add(head);
            Label sub = Lbl("Он бесплатный и таким останется. Если хочется сказать спасибо — можно прислать любую сумму. Это совсем не обязательно.", "dim");
            sub.Font = Fonts.Ui(10f);
            sub.MaximumSize = new Size(S(560), 0);
            sub.Margin = new Padding(0, 0, 0, S(16));
            t.Controls.Add(sub);

            bool hasUrl = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (hasUrl) t.Controls.Add(LinkCard(url));
            var crypto = list.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Address)).ToList();
            if (hasUrl && crypto.Count > 0)
            {
                Label or = Lbl("ИЛИ КРИПТОВАЛЮТОЙ", "section");
                or.Margin = new Padding(0, S(6), 0, S(8));
                t.Controls.Add(or);
            }
            foreach (DonateAddr d in crypto) t.Controls.Add(Card(d));

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(12), 0, 0) };
            var close = new FlatBtn("Закрыть", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            close.Fit();
            buttons.Controls.Add(close);
            t.Controls.Add(buttons);
            CancelButton = close;
        }

        // DonationAlerts: картой, через СБП или кошельком — страница открывается в браузере.
        Control LinkCard(string url)
        {
            var card = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Current.Surface, Tag = "surface", Padding = new Padding(S(16), S(14), S(16), S(14)), Margin = new Padding(0, 0, 0, S(10)) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(330)));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Current.Surface, Tag = "surface" };
            Label title = Lbl("Картой, через СБП или кошельком", "");
            title.Font = Fonts.Semi(12f);
            title.Margin = new Padding(0, 0, 0, S(2));
            Label sub = Lbl("через DonationAlerts — страница откроется в браузере", "dim");
            sub.MaximumSize = new Size(S(320), 0);
            text.Controls.Add(title);
            text.Controls.Add(sub);
            card.Controls.Add(text, 0, 0);
            var open = new FlatBtn("Открыть DonationAlerts", BtnKind.Primary, Glyph.Globe) { Compact = true, Margin = new Padding(S(8), S(6), 0, 0) };
            open.Fit();
            open.Click += delegate
            {
                try { Core.ShellOpen(url); Journal.Write("поблагодарить", "открыта страница DonationAlerts"); }
                catch (Exception ex) { Dlg.Info(this, "Не открылось", ex.Message); }
            };
            card.Controls.Add(open, 1, 0);
            return card;
        }

        Control Card(DonateAddr d)
        {
            string addr = d.Address.Trim();
            var card = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Current.Surface, Tag = "surface", Padding = new Padding(S(14)), Margin = new Padding(0, 0, 0, S(10)) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // QR — для оплаты с телефона: камера кошелька заполнит адрес сама
            var qr = new PictureBox { Size = new Size(S(168), S(168)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Margin = new Padding(0, 0, S(18), 0) };
            try { qr.Image = Qr.Render(addr, S(168)); } catch { }
            card.Controls.Add(qr, 0, 0);

            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Current.Surface, Tag = "surface" };
            Label title = Lbl(d.Title ?? "", "");
            title.Font = Fonts.Semi(12f);
            title.Margin = new Padding(0, 0, 0, S(2));
            right.Controls.Add(title);
            Label net = Lbl(d.Network ?? "", "dim");
            net.Font = Fonts.Semi(9.5f);
            net.ForeColor = Theme.Current.Accent;
            net.Margin = new Padding(0, 0, 0, S(10));
            right.Controls.Add(net);

            // адрес — в поле только для чтения, чтобы его можно было и выделить мышью
            var box = new TextBox
            {
                Text = addr.Substring(0, 24) + "\r\n" + addr.Substring(24), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 11f), BackColor = Theme.Current.Surface, ForeColor = Theme.Current.Text, Width = S(260), Height = S(44),
                Margin = new Padding(0, 0, 0, S(10)), TabStop = false
            };
            if (addr.Length != 48) box.Text = addr;
            right.Controls.Add(box);

            var copy = new FlatBtn("Скопировать адрес", BtnKind.Primary, "") { Compact = true, Margin = new Padding(0, 0, 0, S(8)) };
            copy.Fit();
            Label done = Lbl("", "dim");
            done.Margin = new Padding(0, 0, 0, S(6));
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(addr);
                    done.Text = "Скопировано. Спасибо!";
                    Journal.Write("поблагодарить", "адрес скопирован");
                }
                catch (Exception ex) { done.Text = "Не получилось: " + ex.Message; }
            };
            right.Controls.Add(copy);
            right.Controls.Add(done);

            if (!string.IsNullOrEmpty(d.Note))
            {
                Label note = Lbl(d.Note, "dim");
                note.MaximumSize = new Size(S(320), 0);
                right.Controls.Add(note);
            }
            if (addr.StartsWith("UQ") || addr.StartsWith("EQ"))
                if (!TonAddress.LooksValid(addr))
                {
                    Label bad = Lbl("Похоже, в адресе опечатка — проверьте его в StartPack.json.", "");
                    bad.ForeColor = Theme.Current.Red;
                    right.Controls.Add(bad);
                }
            card.Controls.Add(right, 1, 0);
            return card;
        }
    }
}
