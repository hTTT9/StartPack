// Окна: справка, журнал и отчёт, уборка, ввод названия.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    // ---------------------------------------------------------------- справка

    public class HelpForm : ThemedForm
    {
        static readonly string[][] Sections =
        {
            new[] { "", "Запуск", "«Запустить всё» открывает включённые карточки сверху вниз. ▶ на карточке — запустить только её, ■ — остановить (у VPN — сначала отключит). Порядок меняется перетаскиванием." },
            new[] { "", "Запуск с Windows", "Переключатель внизу справа: после входа в Windows всё из «Запустить всё» стартует само, в фоне. Задержка (чтобы успела подняться сеть) — в «Настройках»." },
            new[] { "", "Профили", "Профиль — набор программ для «Запустить всё»: например, «Всё» и «Только обход блокировок». Переключатель на карточке включает её в текущий профиль." },
            new[] { "", "Трей и Ctrl+Alt+S", "Крестик прячет Старт-пак к часам. Ctrl+Alt+S запускает всё из любой программы. Точка на значке: зелёная — всё работает, жёлтая — не всё, красная — ошибка." },
            new[] { "", "Автоперезапуск", "Если zapret или TG WS Proxy закроются, Старт-пак сам запустит их снова (zapret — после нажатия на уведомление). Включить для любой программы — «⋯» на карточке → «Перезапускать, если закроется»." },
            new[] { "", "zapret", "«Стратегия» на карточке меняет способ обхода блокировок. «Подобрать тестом» сам найдёт лучшую. При обновлении ваши списки переносятся." },
            new[] { "", "Spotify", "Перед запуском проверяет адрес в hosts и чинит его, если он перестал работать." },
            new[] { "", "Связь", "Таблетки YouTube, Discord, Spotify, Telegram: зелёная — отвечает, красная — нет, цифра — пинг в мс. ↻ рядом — проверить ещё раз. Наведите на таблетку, чтобы увидеть причину." },
            new[] { "", "Своя автозагрузка программ", "Discord и Spotify сами стартуют с Windows — раньше zapret. Выключите это в «⋯» на их карточке, и они будут запускаться из Старт-пака по порядку." },
            new[] { "", "Уборка", "«⋯» вверху → «Уборка»: старые версии zapret (где бы они ни лежали) и лишние копии TG WS Proxy — в корзину. Если старую папку держит драйвер zapret, Старт-пак снимет его сам." },
            new[] { "", "Установка программ", "Нет программы? «Установить» на карточке — скачает с официального сайта и проверит подпись. Не получилось — «С сайта»: откроет официальную страницу, установите как обычно, Старт-пак сам её найдёт." },
            new[] { "", "Оверлей", "Панель поверх окон: что запущено, VPN и связь, кнопки ▶/■ и ↻. Ctrl+Alt+O — показать/скрыть. «⋯» вверху → «Настроить оверлей»: что показывать, размер, прозрачность, угол экрана." },
            new[] { "", "VPN: Happ, INCY, Koala Clash", "Подключаются после запуска, если включено автоподключение. Кнопка «Сервер» на карточке: у Koala — номер строки, у Happ — сохранённый сервер («Запомнить» — тот, что выбран в Happ), INCY берёт сервер из своих настроек. В «Запустить всё» VPN не входят, пока не включите тумблер." },
            new[] { "", "Стандартный набор", "«Добавить» → «Стандартный набор» возвращает все программы разом. Чего нет на ПК — появится с кнопкой «Установить»." },
            new[] { "", "Установка и удаление", "StartPack.exe сам предлагает установиться (без прав администратора). Удалить — «Настройки» → «Удалить Старт-пак» или «Параметры Windows» → «Приложения»." },
            new[] { "", "Перенос и сброс", "«Настройки» → «Экспорт» сохранит настройки и список в файл, «Импорт» загрузит их (хоть на другом ПК), «Сброс» вернёт начальные." },
            new[] { "", "Обновления и проект", "Новые версии Старт-пака приходят с GitHub: кнопка «Обновить» вверху, или «Настройки» → «Проверить обновления». Страница проекта — «⋯» → «Страница проекта на GitHub»." },
            new[] { "", "Если что-то не так", "«⋯» вверху → «Журнал и отчёт» → «Скопировать отчёт», затем вставьте текст в чат." },
        };

        public HelpForm()
        {
            Text = "Справка";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(S(620), S(660));
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = S(66) };
            var ok = new FlatBtn("Понятно", BtnKind.Primary, null) { DialogResult = DialogResult.OK };
            ok.Fit();
            buttons.Controls.Add(ok);
            buttons.Resize += delegate { ok.Location = new Point(buttons.Width - S(24) - ok.Width, S(12)); };
            AcceptButton = ok;
            CancelButton = ok;

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(S(24), S(18), S(16), S(6))
            };
            Label head = Lbl("Как пользоваться", "title");
            head.Margin = new Padding(0, 0, 0, S(12));
            flow.Controls.Add(head);
            foreach (string[] s in Sections)
            {
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, S(12)) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(38)));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var icon = new Label { Text = s[0], Font = Fonts.Icons(14f), ForeColor = Theme.Current.Accent, AutoSize = true, Margin = new Padding(0, S(2), 0, 0), Tag = "accent" };
                var title = Lbl(s[1], "");
                title.Font = Fonts.Semi(10.5f);
                title.Margin = new Padding(0, 0, 0, S(2));
                Label text = Lbl(s[2], "");
                text.ForeColor = Theme.Current.TextDim;
                text.Tag = "dim";
                text.Font = Fonts.Ui(10f);
                text.MaximumSize = new Size(S(520), 0);
                text.Margin = new Padding(0);
                row.Controls.Add(icon, 0, 0);
                row.SetRowSpan(icon, 2);
                row.Controls.Add(title, 1, 0);
                row.Controls.Add(text, 1, 1);
                flow.Controls.Add(row);
            }
            Controls.Add(flow);
            Controls.Add(buttons);
            flow.HandleCreated += delegate { Win32.Scrollbars(flow); };
        }
    }

    // ---------------------------------------------------------------- журнал и отчёт

    public class JournalForm : ThemedForm
    {
        readonly Func<string> report;
        readonly TextBox box;
        readonly Label lblInfo;

        public JournalForm(Func<string> report)
        {
            this.report = report;
            Text = "Журнал";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(S(820), S(600));
            MinimumSize = SizeFromClientSize(new Size(S(560), S(380)));

            var top = new Panel { Dock = DockStyle.Top, Height = S(78) };
            Label head = Lbl("Журнал", "title");
            head.Location = new Point(S(24), S(14));
            Label sub = Lbl("Что, когда и с каким результатом делал Старт-пак. Если что-то не так — скопируйте отчёт и пришлите его.", "dim");
            sub.Location = new Point(S(25), S(46));
            top.Controls.Add(head);
            top.Controls.Add(sub);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = S(68) };
            var copy = new FlatBtn("Скопировать отчёт", BtnKind.Primary, "");
            copy.Fit();
            copy.Location = new Point(S(24), S(14));
            copy.Click += delegate { CopyReport(); };
            var folder = new FlatBtn("Папка журнала", BtnKind.Secondary, Glyph.Folder);
            folder.Fit();
            folder.Location = new Point(copy.Right + S(10), S(14));
            folder.Click += delegate { try { Directory.CreateDirectory(Journal.Dir); Core.ShellOpen(Journal.Dir); } catch { } };
            var close = new FlatBtn("Закрыть", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            close.Fit();
            bottom.Resize += delegate { close.Location = new Point(bottom.Width - S(24) - close.Width, S(14)); };
            lblInfo = Lbl("", "dim");
            lblInfo.Location = new Point(folder.Right + S(14), S(25));
            bottom.Controls.AddRange(new Control[] { copy, folder, lblInfo, close });
            CancelButton = close;

            var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), 0, S(24), 0) };
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12), S(10), S(4), S(10)), BackColor = Theme.Current.Surface, Tag = "surface" };
            box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, WordWrap = false,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), BackColor = Theme.Current.Surface, ForeColor = Theme.Current.Text
            };
            box.HandleCreated += delegate { Win32.Scrollbars(box); };
            List<string> lines = Journal.Tail(400);
            box.Text = lines.Count == 0 ? "Пока пусто." : string.Join("\r\n", lines);
            frame.Controls.Add(box);
            holder.Controls.Add(frame);

            Controls.Add(holder);
            Controls.Add(bottom);
            Controls.Add(top);
            Shown += delegate { box.SelectionStart = box.TextLength; box.ScrollToCaret(); };
        }

        void CopyReport()
        {
            try
            {
                Clipboard.SetText(report());
                lblInfo.Text = "Скопировано — вставьте в чат.";
                Journal.Write("отчёт", "скопирован в буфер обмена");
            }
            catch (Exception ex) { lblInfo.Text = "Не получилось: " + ex.Message; }
        }
    }

    // ---------------------------------------------------------------- уборка

    public class CleanupForm : ThemedForm
    {
        public bool Changed;
        readonly bool dryRun;
        readonly Action<Item> expect;
        readonly List<KeyValuePair<CleanupTask, Toggle>> rows = new List<KeyValuePair<CleanupTask, Toggle>>();
        readonly Label result;
        readonly FlatBtn go;

        public CleanupForm(Config cfg, bool dryRun, Action<Item> expect = null)
        {
            this.dryRun = dryRun;
            this.expect = expect;
            Text = "Уборка";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(26), S(18), S(26), S(22)) };
            Controls.Add(t);
            Label head = Lbl("Уборка", "title");
            head.Margin = new Padding(0, 0, 0, S(4));
            t.Controls.Add(head);
            Label sub = Lbl("Отметьте, что убрать. Файлы уходят в корзину — их можно вернуть.", "dim");
            sub.Margin = new Padding(0, 0, 0, S(14));
            t.Controls.Add(sub);

            List<CleanupTask> tasks = Cleanup.Scan(cfg);
            if (tasks.Count == 0)
            {
                Label ok = Lbl("Всё чисто — убирать нечего.", "");
                ok.Margin = new Padding(0, 0, 0, S(8));
                t.Controls.Add(ok);
            }
            foreach (CleanupTask task in tasks)
            {
                var tg = new Toggle(task.Title, task.Hint, true) { Margin = new Padding(0, S(4), 0, S(8)) };
                tg.Fit();
                rows.Add(new KeyValuePair<CleanupTask, Toggle>(task, tg));
                t.Controls.Add(tg);
            }
            result = Lbl("", "dim");
            result.MaximumSize = new Size(S(520), 0);
            result.Margin = new Padding(0, S(6), 0, 0);
            t.Controls.Add(result);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(16), 0, 0) };
            var close = new FlatBtn("Закрыть", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            close.Fit();
            go = new FlatBtn("Убрать отмеченное", BtnKind.Primary, Glyph.Delete) { Margin = new Padding(0, 3, S(10), 3), Visible = tasks.Count > 0 };
            go.Fit();
            go.Click += async delegate { await Apply(); };
            buttons.Controls.Add(close);
            buttons.Controls.Add(go);
            t.Controls.Add(buttons);
            CancelButton = close;
        }

        public async Task Apply()
        {
            var done = new List<string>();
            var failed = new List<string>();
            go.Enabled = false;
            UseWaitCursor = true;
            foreach (var r in rows)
            {
                if (!r.Value.Checked || !r.Value.Enabled) continue;
                CleanupTask task = r.Key;
                try
                {
                    if (task.Kind == "move-tgws") MoveTgWs(task.Item);
                    else
                    {
                        if (task.Kind == "zapret-old") await ReleaseZapret(task);
                        foreach (string p in task.Paths)
                        {
                            if (dryRun) Journal.Write("уборка", "(проверка) было бы в корзину: " + p);
                            else await RecycleRetry(p);
                        }
                        if (task.Kind == "zapret-old") Detect.ForgetZapret();
                    }
                    done.Add(task.Title);
                    r.Value.Enabled = false;
                }
                catch (Exception ex)
                {
                    failed.Add(task.Title + " — " + ex.Message);
                    Journal.Write("уборка", "не получилось: " + task.Title + " — " + ex.Message);
                }
            }
            UseWaitCursor = false;
            result.Text = (done.Count > 0 ? "Готово: " + string.Join("; ", done) + "." : "") +
                (failed.Count > 0 ? (done.Count > 0 ? "\n" : "") + "Не получилось: " + string.Join("; ", failed) + "." : "");
            result.ForeColor = failed.Count > 0 ? Theme.Current.Red : Theme.Current.TextDim;
            go.Enabled = rows.Any(r => r.Value.Enabled);
            if (failed.Count > 0 && go.Enabled) go.Text = "Повторить";
        }

        // Драйвер WinDivert / служба zapret из старой папки держат её файлы: снять их и запустить zapret из текущей папки.
        async Task ReleaseZapret(CleanupTask task)
        {
            ZapretCtl.ForgetServices();
            var svcs = ZapretCtl.Inside(task.Paths);
            if (svcs.Count == 0) return;
            bool wasRunning = Core.RunningNames().Contains("winws");
            string bat = Core.Expand(task.Item.Path);
            if (dryRun) { Journal.Write("уборка", "(проверка) сняли бы " + ZapretCtl.Describe(svcs) + (wasRunning ? " и перезапустили zapret" : "")); return; }
            result.ForeColor = Theme.Current.TextDim;
            result.Text = "Снимаю " + ZapretCtl.Describe(svcs) + "…";
            if (expect != null && wasRunning) expect(task.Item);
            Process p;
            try { p = ZapretCtl.ResetServices(svcs, wasRunning && File.Exists(bat) ? bat : null); }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new Exception("старая папка занята: из неё работает " + ZapretCtl.Describe(svcs) + ". Чтобы его снять, нужно разрешить запуск от администратора");
            }
            if (p != null) using (p) await Task.Run(() => p.WaitForExit(30000));
            await Task.Delay(800);
        }

        // Драйвер выгружается не мгновенно — несколько попыток.
        static async Task RecycleRetry(string path)
        {
            for (int i = 0; ; i++)
            {
                try { Cleanup.Recycle(path); return; }
                catch (IOException) { if (i >= 4) throw; }
                await Task.Delay(1000);
            }
        }

        void MoveTgWs(Item it)
        {
            string src = Core.Expand(it.Path);
            Directory.CreateDirectory(Templates.AppsDir);
            string dest = Path.Combine(Templates.AppsDir, Path.GetFileName(src));
            // пробный прогон ничего не трогает: файл мог быть настоящим, а не тестовой копией
            if (dryRun) { Journal.Write("уборка", "(проверка) перенесли бы " + src + " → " + dest); return; }
            var procs = Core.ProcNames(it).SelectMany(n => Process.GetProcessesByName(n)).ToList();
            bool wasRunning = procs.Count > 0;
            if (expect != null) expect(it);
            foreach (Process p in procs) { try { p.Kill(); p.WaitForExit(5000); } catch { } p.Dispose(); }
            if (File.Exists(dest)) File.Delete(dest);
            File.Copy(src, dest);
            Cleanup.Recycle(src);
            it.Path = dest;
            Changed = true;
            Journal.Write("уборка", "TG WS Proxy перенесён: " + src + " → " + dest);
            if (wasRunning) Core.Launch(it);
        }
    }

    // ---------------------------------------------------------------- ввод названия

    public class InputForm : ThemedForm
    {
        readonly TextField field;

        public static string Ask(IWin32Window owner, string title, string text, string value)
        {
            using (var f = new InputForm(title, text, value))
                return f.ShowDialog(owner) == DialogResult.OK ? f.field.Text : null;
        }

        public InputForm(string title, string text, string value)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(24), S(18), S(24), S(20)) };
            Controls.Add(t);
            Label head = Lbl(title, "title");
            head.Margin = new Padding(0, 0, 0, S(6));
            Label body = Lbl(text, "dim");
            body.MaximumSize = new Size(S(420), 0);
            body.Margin = new Padding(0, 0, 0, S(12));
            field = new TextField(value, S(420)) { Margin = new Padding(0) };
            t.Controls.Add(head);
            t.Controls.Add(body);
            t.Controls.Add(field);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(16), 0, 0) };
            var cancel = new FlatBtn("Отмена", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            cancel.Fit();
            var ok = new FlatBtn("Готово", BtnKind.Primary, null) { DialogResult = DialogResult.OK, Margin = new Padding(0, 3, S(10), 3) };
            ok.Fit();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            t.Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { field.Box.Focus(); field.Box.SelectAll(); };
        }
    }
}
