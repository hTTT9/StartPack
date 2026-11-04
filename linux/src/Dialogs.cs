// Окна: возможности, настройки, добавить и изменить программу, официальный сайт.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace StartPack.Linux;

public static class Dialogs
{
    static Control Sep() => new Border { Height = 1, Background = Pal.Border, Margin = new Thickness(0, 4) };
    static TextBlock Section(string s) => Ui.Text(s, 12, Pal.TextDim, FontWeight.SemiBold);

    static Control Toggle(string title, string hint, bool on, Action<bool> set)
    {
        var t = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 12, 0) };
        t.IsCheckedChanged += (_, _) => set(t.IsChecked == true);
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(Ui.Text(title, 14));
        if (hint != null) text.Children.Add(Ui.Text(hint, 12.5, Pal.TextDim, wrap: true));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(t);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return g;
    }

    static Control Choice(string[] names, string[] values, string current, Action<string> set)
    {
        var row = new WrapPanel();
        var btns = new List<Button>();
        for (int i = 0; i < names.Length; i++)
        {
            string v = values[i];
            Button b = Ui.Btn(names[i], v == current ? Kind.Primary : Kind.Secondary);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) => { set(v); foreach (var x in btns) x.Opacity = 1; };
            btns.Add(b);
            row.Children.Add(b);
        }
        return row;
    }

    // ---------------------------------------------------------------- возможности

    static readonly (string, string)[] HelpItems =
    {
        ("Запуск", "«Запустить всё» запускает включённые карточки сверху вниз. Кнопка справа на карточке запускает или останавливает одну программу."),
        ("Запуск с системой", "Переключатель внизу справа: после входа в систему всё из «Запустить всё» запустится само."),
        ("zapret", "Обход блокировок Discord и YouTube — перенос стратегий Flowseal для Linux. «Стратегия» на карточке меняет способ обхода. Запуск и остановка требуют пароль администратора: zapret работает системной службой."),
        ("VPN: Cloudflare WARP и Happ", "WARP подключается и отключается прямо с карточки. Happ на Linux снаружи не подключить — Старт-пак откроет его, а подключитесь вы в его окне. Пока VPN подключён, zapret на паузе: вместе они ломают Discord и YouTube."),
        ("Установка программ", "«Установить» на карточке: из Flathub, из репозитория системы или из официального выпуска на GitHub с проверкой контрольной суммы. Не получилось — «⋯» → «Скачать с официального сайта»."),
        ("Связь", "Таблетки YouTube, Discord, Spotify, Telegram: зелёная — отвечает, красная — нет. ↻ — проверить ещё раз, наведите на таблетку — причина."),
        ("Если что-то не так", "Журнал — «Настройки» → «Открыть журнал». Его можно приложить к сообщению об ошибке на GitHub."),
    };

    public static async Task Help(Window owner)
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Ui.Text("Как пользоваться", 20, weight: FontWeight.SemiBold));
        bool first = true;
        foreach (var (t, x) in HelpItems)
        {
            if (!first) p.Children.Add(Sep());
            first = false;
            var s = new StackPanel { Spacing = 3 };
            s.Children.Add(Ui.Text(t, 15, weight: FontWeight.SemiBold));
            s.Children.Add(Ui.Text(x, 13.5, Pal.TextDim, wrap: true));
            p.Children.Add(s);
        }
        Window w = Ui.Dialog("Возможности", new ScrollViewer { Content = p, MaxHeight = 620 }, 620);
        var ok = Ui.Btn("Понятно", Kind.Primary);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 10, 0, 0);
        ok.Click += (_, _) => w.Close();
        p.Children.Add(ok);
        await w.ShowDialog(owner);
    }

    // ---------------------------------------------------------------- настройки

    public static async Task Settings(Window owner, Engine e)
    {
        var c = e.Cfg;
        var p = new StackPanel { Spacing = 14 };
        p.Children.Add(Ui.Text("Настройки", 20, weight: FontWeight.SemiBold));

        p.Children.Add(Section("ТЕМА"));
        p.Children.Add(Choice(new[] { "Тёмная", "Светлая", "Как в системе" }, new[] { "dark", "light", "system" }, c.Theme, v =>
        {
            c.Theme = v; e.Save();
            Pal.Apply(Application.Current!, v);
            owner.Background = Pal.Bg;
        }));

        p.Children.Add(Section("ЗАПУСК"));
        p.Children.Add(Toggle("Не держать zapret и VPN включёнными вместе", "Вместе они ломают Discord и YouTube. VPN подключился — zapret останавливается, отключился — запускается снова.", c.PauseZapretForVpn, v => { c.PauseZapretForVpn = v; e.Save(); }));
        p.Children.Add(Toggle("Проверять связь после запуска", "YouTube, Discord, Spotify и Telegram — зелёная точка значит, что отвечают.", c.NetCheck, v => { c.NetCheck = v; e.Save(); }));
        p.Children.Add(Toggle("Проверять обновления при каждом запуске", "Если есть новая версия Старт-пака, вверху появится кнопка «Обновить».", c.CheckUpdates, v => { c.CheckUpdates = v; e.Save(); }));

        p.Children.Add(Section("ZAPRET"));
        var ifaces = Zapret.Interfaces();
        var cb = new ComboBox { ItemsSource = ifaces.Select(i => i == "any" ? "любой (рекомендуется)" : i).ToList(), SelectedIndex = Math.Max(0, ifaces.IndexOf(c.Zapret.Interface)), MinWidth = 260 };
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { c.Zapret.Interface = ifaces[cb.SelectedIndex]; e.Save(); } };
        var ifRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        ifRow.Children.Add(Ui.Text("Сетевой интерфейс", 14));
        ifRow.Children.Add(cb);
        p.Children.Add(ifRow);
        p.Children.Add(Toggle("Игровой фильтр TCP", "Обход и для игр (порты выше 1024). Включайте, если не работают игры, — иначе лучше оставить выключенным.", c.Zapret.GameFilterTcp, v => { c.Zapret.GameFilterTcp = v; e.Save(); }));
        p.Children.Add(Toggle("Игровой фильтр UDP", null, c.Zapret.GameFilterUdp, v => { c.Zapret.GameFilterUdp = v; e.Save(); }));
        p.Children.Add(Ui.Text("Изменения применятся при следующем запуске zapret.", 12.5, Pal.TextDim));

        p.Children.Add(Section("О ПРОГРАММЕ"));
        p.Children.Add(Ui.Text("Старт-пак для Linux " + App.Version + " (бета) · " + Sys.Os().Name, 14));
        var links = new WrapPanel();
        var gh = Ui.Btn("Проект на GitHub", Kind.Secondary, "🌐"); gh.Margin = new Thickness(0, 0, 8, 8);
        gh.Click += (_, _) => Sys.Open("https://github.com/" + Engine.ProjectRepo);
        var log = Ui.Btn("Открыть журнал"); log.Margin = new Thickness(0, 0, 8, 8);
        log.Click += (_, _) => Sys.Open(Log.FilePath);
        var thanks = Ui.Btn("Поблагодарить", Kind.Ghost, "♡"); thanks.Margin = new Thickness(0, 0, 8, 8);
        thanks.Click += (_, _) => Sys.Open("https://www.donationalerts.com/c/hornetdx");
        links.Children.Add(gh); links.Children.Add(log); links.Children.Add(thanks);
        p.Children.Add(links);

        Window w = Ui.Dialog("Настройки", new ScrollViewer { Content = p, MaxHeight = 700 }, 640);
        var done = Ui.Btn("Готово", Kind.Primary);
        done.HorizontalAlignment = HorizontalAlignment.Right;
        done.Click += (_, _) => w.Close();
        p.Children.Add(done);
        await w.ShowDialog(owner);
    }

    // ---------------------------------------------------------------- добавить

    public static int AddStandard(Engine e)
    {
        int n = 0;
        foreach (Known k in Catalog.All)
        {
            if (e.Cfg.Items.Any(i => i.Kind == k.Kind)) continue;
            e.Cfg.Items.Add(Catalog.Make(k, Catalog.Detect(k)));
            n++;
        }
        // VPN — в конец списка
        var vpn = e.Cfg.Items.Where(i => i.IsVpn).ToList();
        e.Cfg.Items.RemoveAll(i => i.IsVpn);
        e.Cfg.Items.AddRange(vpn.OrderBy(i => i.Kind == "warp" ? 0 : 1));
        e.Save();
        e.Refresh();
        return n;
    }

    public static async Task Add(Window owner, Engine e)
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Ui.Text("Добавить", 20, weight: FontWeight.SemiBold));
        Window w = Ui.Dialog("Добавить", p, 520);
        var std = Ui.Btn("Стандартный набор — все программы", Kind.Primary, "★");
        std.Click += (_, _) => { int n = AddStandard(e); w.Close(); };
        p.Children.Add(std);
        p.Children.Add(Ui.Text("zapret, TG WS Proxy, Spotify, Telegram, Discord, Cloudflare WARP, Happ. Чего нет на компьютере — появится с кнопкой «Установить».", 13, Pal.TextDim, wrap: true));
        p.Children.Add(Sep());
        var own = Ui.Btn("Своя программа", Kind.Secondary, "+");
        own.Click += async (_, _) =>
        {
            w.Close();
            var it = new Item { Name = "", Command = "" };
            if (await Edit(owner, e, it, true)) { e.Cfg.Items.Insert(e.Cfg.Items.Count(i => !i.IsVpn), it); e.Save(); e.Refresh(); }
        };
        p.Children.Add(own);
        await w.ShowDialog(owner);
    }

    // ---------------------------------------------------------------- изменить

    public static async Task<bool> Edit(Window owner, Engine e, Item it, bool isNew = false)
    {
        bool known = Catalog.Get(it.Kind) != null;
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(Ui.Text(isNew ? "Своя программа" : "Изменить: " + it.Name, 20, weight: FontWeight.SemiBold));
        TextBox Field(string label, string value, string hint = null)
        {
            p.Children.Add(Ui.Text(label, 13.5, Pal.TextDim));
            var tb = new TextBox { Text = value ?? "", Watermark = hint };
            p.Children.Add(tb);
            return tb;
        }
        TextBox name = Field("Название", it.Name);
        TextBox cmd = Field("Что запускать", it.Command, "/usr/bin/программа или flatpak:org.id");
        if (it.Kind == "zapret") cmd.IsEnabled = false;
        var browse = Ui.Btn("Выбрать файл");
        browse.Click += async (_, _) =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Что запускать", AllowMultiple = false });
            if (files.Count > 0)
            {
                string path = files[0].TryGetLocalPath();
                if (path != null)
                {
                    cmd.Text = path;
                    if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(path);
                }
            }
        };
        if (it.Kind != "zapret") p.Children.Add(browse);
        TextBox args = Field("Параметры", it.Args);
        TextBox proc = Field("Процесс", it.Process, "имя процесса — по нему видно, что программа работает");
        if (known) { proc.IsEnabled = false; p.Children.Add(Ui.Text("Тип и имя процесса определены по самой программе.", 12.5, Pal.TextDim)); }
        var delay = new NumericUpDown { Value = it.Delay, Minimum = 0, Maximum = 120, Increment = 1, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Ui.Text("Пауза после запуска, сек", 13.5, Pal.TextDim));
        p.Children.Add(delay);
        if (it.Kind == "warp")
        {
            var ac = new CheckBox { Content = "Подключать после запуска", IsChecked = it.AutoConnect };
            ac.IsCheckedChanged += (_, _) => it.AutoConnect = ac.IsChecked == true;
            p.Children.Add(ac);
        }

        bool saved = false;
        Window w = Ui.Dialog(isNew ? "Добавить" : "Изменить", p, 560);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = Ui.Btn("Отмена"); cancel.Click += (_, _) => w.Close();
        var ok = Ui.Btn("Сохранить", Kind.Primary);
        ok.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || (it.Kind != "zapret" && string.IsNullOrWhiteSpace(cmd.Text)))
            {
                await Ui.Info(w, "Не хватает данных", "Заполните название и что запускать.");
                return;
            }
            it.Name = name.Text.Trim();
            if (it.Kind != "zapret") it.Command = cmd.Text.Trim();
            it.Args = args.Text?.Trim() ?? "";
            if (!known) it.Process = string.IsNullOrWhiteSpace(proc.Text) ? Path.GetFileName(it.Command.Split(' ')[0]) : proc.Text.Trim();
            it.Delay = (int)(delay.Value ?? 0);
            e.Save();
            saved = true;
            w.Close();
        };
        row.Children.Add(cancel); row.Children.Add(ok);
        p.Children.Add(row);
        await w.ShowDialog(owner);
        return saved;
    }

    // ---------------------------------------------------------------- официальный сайт

    public static async Task Page(Window owner, Known k)
    {
        if (await Ui.Ask(owner, "Скачать " + k.Name + " с официального сайта",
            k.PageHint + "\n\nПосле установки вернитесь в Старт-пак — он сам найдёт программу.\n\n" + k.Page, "Открыть сайт", "Закрыть"))
            Sys.Open(k.Page);
    }
}
