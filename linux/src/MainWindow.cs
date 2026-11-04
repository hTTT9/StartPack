// Главное окно: шапка («Запустить всё»), строка связи, карточки программ, низ («Добавить», «Запуск с системой»).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace StartPack.Linux;

public class MainWindow : Window
{
    readonly Engine e;
    readonly StackPanel cards = new() { Spacing = 10 };
    readonly StackPanel netRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock status;
    readonly TextBlock count;
    readonly Button btnRunAll, btnUpdate;
    readonly ToggleSwitch autoToggle;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    bool runningAll;

    public MainWindow(Engine engine)
    {
        e = engine;
        Title = "Старт-пак";
        Width = 1000; Height = 860; MinWidth = 760; MinHeight = 520;
        Background = Pal.Bg;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://startpack/assets/app-256.png"))); } catch { }

        // ---------- шапка
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(24, 22, 24, 10) };
        Image logo = new() { Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0) };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://startpack/assets/app-256.png"))); } catch { }
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleRow.Children.Add(Ui.Text("Старт-пак", 24, weight: FontWeight.SemiBold));
        titleRow.Children.Add(new Border
        {
            Background = Pal.SurfaceAlt, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Text("Linux · бета", 12, Pal.TextDim)
        });
        titles.Children.Add(titleRow);
        status = Ui.Text("Нажмите «Запустить всё» или запускайте программы по одной.", 13.5, Pal.TextDim);
        titles.Children.Add(status);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        btnUpdate = Ui.Btn("Обновить", Kind.Secondary, "↓", 14.5);
        btnUpdate.IsVisible = false;
        btnUpdate.Click += async (_, _) => await DoUpdate();
        btnRunAll = Ui.Btn("Запустить всё", Kind.Primary, "▶", 15);
        btnRunAll.Padding = new Thickness(20, 11);
        btnRunAll.Click += async (_, _) => await RunAll();
        var help = Ui.Btn("Возможности", Kind.Secondary, "💡", 14);
        help.Padding = new Thickness(16, 11);
        help.Click += async (_, _) => await Dialogs.Help(this);
        var settings = Ui.Btn("", Kind.Ghost, "⚙", 18);
        settings.Padding = new Thickness(10, 8);
        ToolTip.SetTip(settings, "Настройки");
        settings.Click += async (_, _) => { await Dialogs.Settings(this, e); Rebuild(); };
        actions.Children.Add(btnUpdate);
        actions.Children.Add(btnRunAll);
        actions.Children.Add(help);
        actions.Children.Add(settings);
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);

        // ---------- строка связи
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(24, 4, 24, 12) };
        var refresh = Ui.Btn("", Kind.Ghost, "↻", 18);
        refresh.Padding = new Thickness(8, 4);
        ToolTip.SetTip(refresh, "Проверить связь с сервисами ещё раз");
        refresh.Click += async (_, _) => await e.CheckNet();
        Grid.SetColumn(refresh, 1);
        Grid.SetColumn(netRow, 2);
        bar.Children.Add(refresh);
        bar.Children.Add(netRow);

        // ---------- карточки
        var scroll = new ScrollViewer { Content = cards, Margin = new Thickness(24, 0, 16, 0), Padding = new Thickness(0, 0, 8, 0) };

        // ---------- низ
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(24, 12, 24, 18) };
        var add = Ui.Btn("Добавить", Kind.Secondary, "+", 14.5);
        add.Padding = new Thickness(16, 10);
        add.Click += async (_, _) => { await Dialogs.Add(this, e); Rebuild(); };
        footer.Children.Add(add);
        count = Ui.Text("", 12.5, Pal.TextDim);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(count, 1);
        footer.Children.Add(count);
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        autoToggle = new ToggleSwitch { IsChecked = Sys.AutostartEnabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        autoToggle.IsCheckedChanged += (_, _) =>
        {
            try { Sys.SetAutostart(autoToggle.IsChecked == true); } catch (Exception ex) { Say("Не получилось: " + ex.Message); }
            Say(Sys.AutostartEnabled ? "Готово: после входа в систему всё из «Запустить всё» запустится само." : "Запуск с системой выключен.");
        };
        autoRow.Children.Add(autoToggle);
        autoRow.Children.Add(Ui.Text("Запуск с системой", 14));
        ToolTip.SetTip(autoRow, "После входа в систему Старт-пак сам запустит всё из «Запустить всё»");
        Grid.SetColumn(autoRow, 2);
        footer.Children.Add(autoRow);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(scroll);
        Content = root;

        e.Changed += () => Dispatcher.UIThread.Post(Rebuild);
        e.Status += s => Dispatcher.UIThread.Post(() => Say(s));
        timer.Tick += (_, _) => Tick();
        Opened += async (_, _) => await Startup();
        e.Refresh();
        Rebuild();
    }

    public void Say(string s) => status.Text = s;

    async Task Startup()
    {
        timer.Start();
        if (App.Updated) Say("Старт-пак обновлён до " + App.Version + " — список программ и настройки на месте.");
        else if (Store.FirstRun)
        {
            var missing = e.Cfg.Items.Where(i => !Catalog.Exists(i)).Select(i => i.Name).ToList();
            if (missing.Count > 0) Say("Не установлены: " + string.Join(", ", missing) + " — кнопка «Установить» на карточке.");
        }
        if (e.Cfg.NetCheck) _ = e.CheckNet();
        if (e.Cfg.CheckUpdates) await e.CheckUpdate();
    }

    void Tick()
    {
        if (File.Exists(SingleInstance.ShowRequest)) { try { File.Delete(SingleInstance.ShowRequest); } catch { } Activate(); }
        if (e.Cfg.Items.Any(i => i.Busy)) return;
        e.Refresh();
        if (e.ConflictToWarn()) _ = WarnConflict();
    }

    async Task WarnConflict()
    {
        string v = string.Join(", ", e.Cfg.Items.Where(x => x.IsVpn && Vpn.Connected(x, e.Procs)).Select(x => x.Name));
        if (await Ui.Ask(this, "zapret и VPN включены вместе",
            "Подключён " + v + ", и работает zapret. Вместе они ломают Discord и YouTube: Discord может падать или бесконечно переподключаться.\n\nОстановить zapret, пока подключён VPN? Когда VPN отключится, zapret запустится снова.",
            "Остановить zapret", "Оставить как есть"))
            await e.StopZapretForVpn();
    }

    async Task RunAll()
    {
        if (runningAll) return;
        runningAll = true;
        btnRunAll.IsEnabled = false;
        try { await e.RunAll(); }
        finally { runningAll = false; btnRunAll.IsEnabled = true; }
    }

    async Task DoUpdate()
    {
        if (e.Update == null) return;
        string v = e.Update.Tag.Replace("linux-v", "");
        if (!Engine.IsAppImage)
        {
            await Ui.Info(this, "Вышла версия " + v, "Старт-пак установлен пакетом. Откроется страница выпуска: скачайте новый .deb или .rpm и установите его так же, как первый раз. Список программ сохранится.");
            Sys.Open(e.Update.Page);
            return;
        }
        if (!await Ui.Ask(this, "Обновить Старт-пак до " + v + "?", "Новая версия скачается с GitHub, проверится по контрольной сумме и откроется. Список программ и настройки сохранятся.", "Обновить")) return;
        try
        {
            string r = await e.ApplyUpdate(s => Say(s));
            if (r == null) Close();
        }
        catch (Exception ex) { await Ui.Info(this, "Не получилось обновить", ex.Message); }
    }

    // ---------------------------------------------------------------- перерисовка

    void Rebuild()
    {
        btnUpdate.IsVisible = e.Update != null;
        int on = e.Cfg.Items.Count(i => i.Enabled);
        count.Text = e.Cfg.Items.Count + " в списке · " + on + " в «Запустить всё»";

        netRow.Children.Clear();
        foreach (NetTarget t in e.Net)
        {
            IBrush c = t.State == "ok" ? Pal.Green : t.State == "fail" ? Pal.Red : t.State == "checking" ? Pal.Blue : Pal.TextFaint;
            string label = t.State switch
            {
                "ok" => t.Ms >= 0 ? t.Ms + " мс" : "работает",
                "fail" => "нет связи",
                "checking" => "…",
                _ => ""
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(Ui.Dot(c));
            row.Children.Add(Ui.Text(t.Name, 13, t.State == "fail" ? Pal.Red : Pal.Text, FontWeight.SemiBold));
            if (label.Length > 0) row.Children.Add(Ui.Text(label, 13, t.State == "fail" ? Pal.Red : Pal.TextDim));
            var pill = new Border
            {
                Background = Pal.Surface, BorderBrush = Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15),
                Padding = new Thickness(12, 6), MinWidth = 120, Child = row
            };
            ToolTip.SetTip(pill, t.State == "fail" ? t.Name + ": " + t.Error : t.Name == "Telegram" ? "Telegram через TG WS Proxy" : t.Name);
            netRow.Children.Add(pill);
        }

        cards.Children.Clear();
        if (e.Cfg.Items.Count == 0)
        {
            var empty = new StackPanel { Spacing = 14, Margin = new Thickness(0, 80, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(Ui.Text("Список пуст.", 15, Pal.TextDim));
            var std = Ui.Btn("Стандартный набор", Kind.Primary, "★");
            std.HorizontalAlignment = HorizontalAlignment.Center;
            std.Click += (_, _) => { Dialogs.AddStandard(e); Rebuild(); };
            empty.Children.Add(std);
            cards.Children.Add(empty);
            return;
        }
        foreach (Item it in e.Cfg.Items.ToList()) cards.Children.Add(Card(it));
    }

    Control Card(Item it)
    {
        bool exists = Catalog.Exists(it);
        bool run = exists && e.IsRunning(it);
        bool conn = it.IsVpn && Vpn.Connected(it, e.Procs);

        // состояние
        IBrush dot; string st;
        if (it.Busy && it.Message != null) { dot = Pal.Blue; st = it.Message; }
        else if (!exists) { dot = Pal.TextFaint; st = "не установлена"; }
        else if (it.Message != null && it.MessageIsError) { dot = Pal.Red; st = it.Message; }
        else if (it.IsVpn) { dot = conn ? Pal.Green : run ? Pal.Amber : Pal.TextFaint; st = conn ? "подключено" : run ? "открыт, не подключён" : it.Message ?? "не запущен"; }
        else if (run) { dot = Pal.Green; st = it.Kind == "zapret" ? "работает" : "работает"; }
        else { dot = Pal.TextFaint; st = it.Message ?? (it.Kind == "zapret" ? "не запущен" : "не запущена"); }

        var detail = new List<string>();
        if (it.Kind == "zapret" && exists) detail.Add("стратегия " + Zapret.Pretty(e.Cfg.Zapret.Strategy));
        if (it.IsVpn) detail.Add(it.AutoConnect && it.Kind == "warp" ? "автоподключение" : it.Kind == "happ" ? "подключение — в окне Happ" : "без автоподключения");
        if (it.Command.StartsWith("flatpak:")) detail.Add("Flatpak");

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto"), VerticalAlignment = VerticalAlignment.Center };

        var toggle = new ToggleSwitch { IsChecked = it.Enabled, OnContent = "", OffContent = "", MinWidth = 0, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(toggle, it.Enabled ? "Участвует в «Запустить всё» — нажмите, чтобы исключить" : "Не участвует в «Запустить всё» — нажмите, чтобы включить");
        toggle.IsCheckedChanged += (_, _) => { it.Enabled = toggle.IsChecked == true; e.Save(); Rebuild(); };
        g.Children.Add(toggle);

        var tile = Ui.Tile(it.Kind, it.Name);
        tile.Opacity = it.Enabled ? 1 : 0.5;
        tile.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(tile, 1);
        g.Children.Add(tile);

        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(it.Name, 15, it.Enabled ? Pal.Text : Pal.TextFaint, FontWeight.SemiBold));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        line.Children.Add(Ui.Dot(dot));
        line.Children.Add(Ui.Text(st, 13, dot == Pal.Red ? Pal.Red : it.Enabled ? Pal.Text : Pal.TextFaint));
        if (detail.Count > 0) line.Children.Add(Ui.Text("·   " + string.Join("   ·   ", detail), 13, Pal.TextDim));
        text.Children.Add(line);
        Grid.SetColumn(text, 2);
        g.Children.Add(text);

        // кнопки-действия
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 8, 0) };
        if (!exists && !it.Busy)
        {
            var inst = Ui.Btn("Установить");
            inst.Click += async (_, _) => await Install(it);
            chips.Children.Add(inst);
        }
        if (it.Kind == "zapret" && exists && !it.Busy)
        {
            var strat = Ui.Btn("Стратегия: " + Zapret.Pretty(e.Cfg.Zapret.Strategy));
            var menu = new MenuFlyout();
            foreach (string bat in Zapret.Strategies())
            {
                string b = bat;
                var mi = new MenuItem { Header = (b == e.Cfg.Zapret.Strategy ? "✓  " : "     ") + Zapret.Pretty(b) };
                mi.Click += async (_, _) => await e.SwitchStrategy(it, b);
                menu.Items.Add(mi);
            }
            strat.Flyout = menu;
            chips.Children.Add(strat);
        }
        if (it.Kind == "warp" && run && !it.Busy)
        {
            var c = Ui.Btn(conn ? "Отключить" : "Подключить");
            c.Click += async (_, _) => { if (conn) await e.Disconnect(it); else await e.Connect(it); };
            chips.Children.Add(c);
        }
        Grid.SetColumn(chips, 3);
        g.Children.Add(chips);

        if (exists)
        {
            Button act = run
                ? Ui.Btn(it.Kind is "zapret" or "tgws" ? "Остановить" : "Закрыть", Kind.Stop, "stop")
                : Ui.Btn("Запустить", Kind.Start, "▶");
            act.MinWidth = 132;
            act.IsEnabled = !it.Busy;
            act.Click += async (_, _) => { if (run) await e.StopOne(it); else await e.RunOne(it, false); };
            ToolTip.SetTip(act, run ? (it.IsVpn ? "Закрыть полностью (сначала отключит VPN)" : "Остановить программу") : "Запустить только эту программу");
            Grid.SetColumn(act, 4);
            g.Children.Add(act);
        }

        var more = Ui.Btn("", Kind.Ghost, "⋯", 18);
        more.Padding = new Thickness(10, 4);
        more.Margin = new Thickness(6, 0, 0, 0);
        more.Flyout = ItemMenu(it, exists);
        Grid.SetColumn(more, 5);
        g.Children.Add(more);

        var card = new Border
        {
            Background = Pal.Surface, BorderBrush = Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 14), Child = g
        };
        ToolTip.SetTip(card, string.IsNullOrEmpty(it.Command) ? it.Name : it.Command);
        return card;
    }

    MenuFlyout ItemMenu(Item it, bool exists)
    {
        var m = new MenuFlyout();
        void Add(string header, Func<Task> act) { var mi = new MenuItem { Header = header }; mi.Click += async (_, _) => await act(); m.Items.Add(mi); }
        int idx = e.Cfg.Items.IndexOf(it);
        Known k = Catalog.Get(it.Kind);
        if (k?.Page != null) Add("Скачать с официального сайта", async () => await Dialogs.Page(this, k));
        if (it.Kind == "zapret" && exists) Add("Переустановить zapret", async () => await Install(it));
        if (idx > 0) Add("Выше", () => { Move(it, -1); return Task.CompletedTask; });
        if (idx < e.Cfg.Items.Count - 1) Add("Ниже", () => { Move(it, 1); return Task.CompletedTask; });
        Add("Изменить", async () => { await Dialogs.Edit(this, e, it); Rebuild(); });
        Add("Убрать из списка", async () =>
        {
            if (!await Ui.Ask(this, "Убрать «" + it.Name + "»?", "Программа останется на компьютере — уйдёт только карточка из списка.", "Убрать")) return;
            e.Cfg.Items.Remove(it); e.Save(); Rebuild();
        });
        return m;
    }

    void Move(Item it, int d)
    {
        int i = e.Cfg.Items.IndexOf(it), j = i + d;
        if (j < 0 || j >= e.Cfg.Items.Count) return;
        e.Cfg.Items.RemoveAt(i);
        e.Cfg.Items.Insert(j, it);
        e.Save();
        Rebuild();
    }

    async Task Install(Item it)
    {
        Known k = Catalog.Get(it.Kind);
        if (k == null) return;
        string how = k.Kind == "zapret"
            ? "Старт-пак скачает перенос zapret для Linux с GitHub, стратегии Flowseal и nfqws. Если в системе нет git, curl или nftables — поставит их из репозиториев системы (спросит пароль)."
            : k.Flatpak != null && Sys.HasFlatpak ? "Программа поставится из Flathub — пароль не нужен."
            : k.BinAsset != null ? "Старт-пак скачает программу из официального выпуска на GitHub и сверит контрольную сумму. Пароль не нужен."
            : k.GithubRepo != null && Sys.Os().PackageManager != null ? "Старт-пак скачает официальный пакет с GitHub, сверит контрольную сумму и установит его (система спросит пароль)."
            : k.AptPackage != null && Sys.Os().PackageManager == "apt" ? "Программа поставится из репозитория системы (система спросит пароль)."
            : null;
        if (how == null) { await Dialogs.Page(this, k); return; }
        if (!await Ui.Ask(this, "Установить " + k.Name + "?", how, "Установить")) return;
        it.Busy = true; it.Message = "готовлю установку…"; Rebuild();
        string err = await e.Install(it, s => Dispatcher.UIThread.Post(() => { it.Message = s; Rebuild(); }));
        it.Busy = false;
        it.Message = err == null ? null : err == "page" ? null : "не установилась: " + err;
        it.MessageIsError = err != null && err != "page";
        Rebuild();
        if (err == "page") await Dialogs.Page(this, k);
        else if (err == null) Say(k.Name + " установлена.");
        else if (await Ui.Ask(this, "Не получилось установить " + k.Name, err + "\n\nМожно поставить с официального сайта — Старт-пак потом найдёт программу сам.", "Открыть сайт", "Закрыть"))
            Sys.Open(k.Page);
    }
}
