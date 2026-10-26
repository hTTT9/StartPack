// «Настроить оверлей»: что показывать, кнопки, размер, прозрачность, угол экрана. Всё применяется сразу.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StartPack
{
    public class OverlaySettingsForm : ThemedForm
    {
        readonly Config cfg;
        readonly Action onChange;

        public OverlaySettingsForm(Config cfg, Action onChange)
        {
            this.cfg = cfg;
            this.onChange = onChange;
            Text = "Оверлей";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(26), S(18), S(26), S(22)) };
            Controls.Add(t);
            Label head = Lbl("Оверлей", "title");
            head.Margin = new Padding(0, 0, 0, S(4));
            t.Controls.Add(head);
            Label sub = Lbl("Маленькая панель поверх всех окон. Изменения видны сразу.", "dim");
            sub.Margin = new Padding(0, 0, 0, S(10));
            t.Controls.Add(sub);

            AddToggle(t, "Показывать оверлей", "Ctrl+Alt+O — показать или скрыть из любой программы.", cfg.Overlay, on => cfg.Overlay = on);

            t.Controls.Add(Sec("ЧТО ПОКАЗЫВАТЬ"));
            t.Controls.Add(Choice(new[] { "Из «Запустить всё»", "Все программы", "Только запущенные" }, new[] { "enabled", "all", "running" },
                cfg.OverlayShow, v => cfg.OverlayShow = v));
            AddToggle(t, "Состояние программ", "«работает», «подключено», «не запущена» — справа в строке.", cfg.OverlayStatus, on => cfg.OverlayStatus = on);
            AddToggle(t, "Связь с сервисами", "YouTube, Discord, Spotify, Telegram: зелёная точка — отвечают.", cfg.OverlayNet, on => cfg.OverlayNet = on);
            AddToggle(t, "Пинг в миллисекундах", "Цифра рядом с сервисом: чем меньше, тем быстрее он отвечает.", cfg.OverlayMs, on => cfg.OverlayMs = on);
            AddToggle(t, "Кнопки ▶ / ■ и ↻", "Запустить или остановить программу и заново проверить связь прямо из оверлея.", cfg.OverlayButtons, on => cfg.OverlayButtons = on);

            t.Controls.Add(Sec("ВИД"));
            t.Controls.Add(Choice(new[] { "Мелкий", "Обычный", "Крупный" }, new[] { "85", "100", "120" },
                cfg.OverlayScale.ToString(), v => cfg.OverlayScale = int.Parse(v)));
            Label op = Lbl("Прозрачность: " + cfg.OverlayOpacity + "%", "");
            op.Margin = new Padding(0, S(8), 0, S(2));
            t.Controls.Add(op);
            var slider = new Slider(20, 100, cfg.OverlayOpacity) { Margin = new Padding(0, 0, 0, S(6)) };
            slider.ValueChanged += delegate { cfg.OverlayOpacity = slider.Value; op.Text = "Прозрачность: " + slider.Value + "%"; Changed(); };
            t.Controls.Add(slider);
            AddToggle(t, "Пропускать клики сквозь оверлей", "Не мешает работать в окнах под ним. Но кнопки и перетаскивание тогда не работают.", cfg.OverlayClickThrough, on => cfg.OverlayClickThrough = on);

            t.Controls.Add(Sec("ПОЛОЖЕНИЕ"));
            Label pl = Lbl("Перетащите оверлей мышью — или прижмите к углу экрана:", "dim");
            pl.Margin = new Padding(0, 0, 0, S(6));
            t.Controls.Add(pl);
            var corners = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            foreach (var c in new[] { new[] { "↖  Слева сверху", "tl" }, new[] { "↗  Справа сверху", "tr" }, new[] { "↙  Слева снизу", "bl" }, new[] { "↘  Справа снизу", "br" } })
            {
                string id = c[1];
                var b = new FlatBtn(c[0], BtnKind.Secondary, null) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
                b.Fit();
                b.Click += delegate { Snap(id); };
                corners.Controls.Add(b);
            }
            t.Controls.Add(corners);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(20), 0, 0) };
            var done = new FlatBtn("Готово", BtnKind.Primary, null) { DialogResult = DialogResult.OK };
            done.Fit();
            buttons.Controls.Add(done);
            t.Controls.Add(buttons);
            AcceptButton = done;
            CancelButton = done;
        }

        void Changed()
        {
            try { Core.Save(cfg); } catch { }
            if (onChange != null) onChange();
        }

        Label Sec(string text)
        {
            Label l = Lbl(text, "section");
            l.Margin = new Padding(0, S(18), 0, S(8));
            return l;
        }

        void AddToggle(TableLayoutPanel t, string text, string hint, bool on, Action<bool> set)
        {
            var tg = new Toggle(text, hint, on) { Margin = new Padding(0, S(4), 0, S(6)) };
            tg.Fit();
            tg.CheckedChanged += delegate { set(tg.Checked); Changed(); };
            t.Controls.Add(tg);
        }

        // Ряд кнопок «выбери одно».
        Control Choice(string[] names, string[] values, string current, Action<string> set)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, S(6)) };
            var list = new List<FlatBtn>();
            for (int i = 0; i < names.Length; i++)
            {
                string v = values[i];
                var b = new FlatBtn(names[i], v == current ? BtnKind.Primary : BtnKind.Secondary, null) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
                b.Fit();
                b.Click += delegate
                {
                    set(v);
                    foreach (FlatBtn x in list) { x.Kind = x == b ? BtnKind.Primary : BtnKind.Secondary; x.Invalidate(); }
                    Changed();
                };
                list.Add(b);
                row.Controls.Add(b);
            }
            return row;
        }

        // Прижать оверлей к углу того экрана, где он сейчас.
        void Snap(string corner)
        {
            if (!cfg.Overlay) { cfg.Overlay = true; Changed(); }
            OverlayForm ov = Application.OpenForms.OfType<OverlayForm>().FirstOrDefault();
            Size size = ov != null ? ov.Size : new Size(S(300), S(200));
            Screen scr = ov != null ? Screen.FromControl(ov) : Screen.PrimaryScreen;
            Rectangle wa = scr.WorkingArea;
            int m = S(16);
            int x = corner.EndsWith("l") ? wa.Left + m : wa.Right - size.Width - m;
            int y = corner.StartsWith("t") ? wa.Top + m : wa.Bottom - size.Height - m;
            cfg.OverlayX = x;
            cfg.OverlayY = y;
            if (ov != null) ov.Location = new Point(x, y);
            Changed();
        }
    }
}
