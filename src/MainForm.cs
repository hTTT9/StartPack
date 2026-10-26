// Главное окно Старт-пака: раскладка, карточки, меню, правка списка, запуск.
// Остальное — в MainForm.Services.cs (Happ, Spotify, zapret, обновления, связь),
// MainForm.Tray.cs (трей, сторож, горячая клавиша, профили) и MainForm.Test.cs (самопроверка).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm : ThemedForm
    {
        Config cfg;
        readonly bool autorun;
        readonly bool startHidden;
        readonly string testLog;
        readonly string loadWarning;
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string happServerName, happGuid;
        bool runningAll, checking;
        int lastErrors;
        bool stopRun;
        readonly Dictionary<Item, Image> icons = new Dictionary<Item, Image>();
        readonly Dictionary<Item, string> runEntries = new Dictionary<Item, string>();
        ContextMenuStrip lastMenu;

        Panel header, bar, footer, body;
        PictureBox logo;
        Label lblTitle, lblStatus, lblCount, lblHint;
        FlatBtn btnRunAll, btnUpdate, btnSettings, btnHelp, btnMore, btnAdd, btnProfile;
        NetBar netBar;
        FlatBtn btnPing;
        bool demo;   // снимок для README: без личных подробностей
        Toggle autoToggle;
        bool syncingAuto;
        Callout autoTip;
        CardList cards;
        System.Windows.Forms.Timer timer;

        public MainForm(Config cfg, bool autorun, bool startHidden, string testLog, string loadWarning)
        {
            this.cfg = cfg;
            this.autorun = autorun;
            this.startHidden = startHidden;
            this.testLog = testLog;
            this.loadWarning = loadWarning;

            Text = Core.Title;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            // высота — чтобы все карточки помещались без прокрутки (но не выше 90% экрана)
            int want = S(100) + S(52) + S(72) + S(12) + Math.Max(4, cfg.Items.Count) * S(84) + S(8);
            int maxH = (int)(Screen.PrimaryScreen.WorkingArea.Height * 0.9);
            ClientSize = new Size(S(1000), Math.Min(maxH - S(40), want));
            MinimumSize = SizeFromClientSize(new Size(S(800), S(500)));
            RestoreBounds2();
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; } };

            foreach (Item it in cfg.Items) Core.AdoptNewestZapret(it);
            BuildUi();
            ApplyTray();
            RefreshState();
            ApplyOverlay();
            StartBackgroundUpdates();

            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { RefreshState(); };
            timer.Start();

            Shown += delegate { if (!startHidden) Startup(); };
            FormClosing += OnClosing;
            FormClosed += OnClosedCleanup;
            Journal.Write("старт", "Старт-пак " + Core.Version + (autorun ? ", автозапуск" : "") + (startHidden ? ", в трее" : "") + (testLog != null ? ", самопроверка" : ""));
        }

        // ---------------------------------------------------------------- раскладка

        void BuildUi()
        {
            SuspendLayout();
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), S(4), S(24), S(6)) };
            cards = new CardList
            {
                Dock = DockStyle.Fill, AllowDrop = true,
                EmptyText = "Список пуст.\nВерните все программы разом — или перетащите сюда файл или ярлык."
            };
            cards.Describe = Describe;
            cards.IconOf = IconOf;
            btnStandard = new FlatBtn("Стандартный набор", BtnKind.Primary, Glyph.Star) { Visible = false };
            btnStandard.Fit();
            btnStandard.Click += delegate { AddStandardSet(); };
            cards.Controls.Add(btnStandard);
            cards.Resize += delegate { PlaceStandardButton(); };
            cards.DragEnter += OnDragEnter;
            cards.DragDrop += OnDragDrop;
            cards.ToggleClick += it =>
            {
                it.Enabled = !it.Enabled;
                cfg.StoreProfile();
                SaveCfg();
                RefreshState();
            };
            cards.RunClick += async it => await RunSelected(it);
            cards.StopClick += async it => await StopItem(it);
            cards.BadgeClick += async it => await UpdateItem(it);
            cards.MenuClick += ShowItemMenu;
            cards.ChipClick += async (it, id) => await OnChip(it, id);
            cards.Activate += EditItem;
            cards.Reorder += (from, to) =>
            {
                Item moved = cfg.Items[from];
                cfg.Items.RemoveAt(from);
                cfg.Items.Insert(to, moved);
                SaveCfg();
                SyncCards();
            };
            body.Controls.Add(cards);

            // шапка
            header = new Panel { Dock = DockStyle.Top, Height = S(100) };
            logo = new PictureBox { Size = new Size(S(48), S(48)), Location = new Point(S(24), S(24)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            logo.Image = IconLoader.App(S(48));
            lblTitle = Lbl(Core.Title, "");
            lblTitle.Font = Fonts.Semi(17f);
            lblTitle.Location = new Point(logo.Right + S(14), S(20));
            lblStatus = Lbl("", "dim");
            lblStatus.Font = Fonts.Ui(9.75f);
            lblStatus.AutoSize = false;
            lblStatus.AutoEllipsis = true;
            lblStatus.Location = new Point(logo.Right + S(16), S(56));
            lblStatus.Height = S(22);
            btnRunAll = new FlatBtn("Запустить всё", BtnKind.Primary, Glyph.Play) { Font = Fonts.Semi(11f) };
            btnRunAll.Fit();
            btnRunAll.Height = S(44);
            btnRunAll.Width += S(10);
            btnRunAll.Click += delegate
            {
                // пока идёт запуск, кнопка — «Остановить»
                if (runningAll) { stopRun = true; btnRunAll.Text = "Останавливаю…"; btnRunAll.Fit(); btnRunAll.Height = S(44); LayoutHeader(); }
                else RunAll();
            };
            btnUpdate = new FlatBtn("Обновить", BtnKind.Blue, Glyph.Download) { Visible = false };
            btnUpdate.Fit();
            btnUpdate.Height = S(44);
            btnUpdate.Click += async delegate { await UpdateAll(); };
            btnHelp = IconBtn("", "Справка (F1)", delegate { ShowHelp(); });
            btnMore = IconBtn(Glyph.More, "Ещё: журнал и отчёт, уборка, связь, выход", delegate { ShowMoreMenu(); });
            btnSettings = IconBtn(Glyph.Settings, "Настройки: тема, трей, горячая клавиша, автозапуск", delegate { OpenSettings(); });
            header.Controls.AddRange(new Control[] { logo, lblTitle, lblStatus, btnRunAll, btnUpdate, btnHelp, btnMore, btnSettings });
            header.Resize += delegate { LayoutHeader(); };

            // строка: профиль слева, связь справа
            bar = new Panel { Dock = DockStyle.Top, Height = S(52) };
            btnProfile = new FlatBtn("Профиль", BtnKind.Secondary, "") { Compact = true };
            btnProfile.Location = new Point(S(24), S(8));
            btnProfile.Click += delegate { ShowProfileMenu(); };
            new ToolTip().SetToolTip(btnProfile, "Профиль — набор программ для «Запустить всё»");
            netBar = new NetBar { Height = S(36) };
            netBar.Recheck += async delegate { await CheckNet(true); };
            btnPing = new FlatBtn("", BtnKind.Ghost, Glyph.Refresh);
            btnPing.Fit();
            btnPing.Size = new Size(S(36), S(36));
            btnPing.Click += async delegate { await CheckNet(true); };
            new ToolTip().SetToolTip(btnPing, "Проверить связь с сервисами ещё раз");
            bar.Controls.Add(btnProfile);
            bar.Controls.Add(btnPing);
            bar.Controls.Add(netBar);
            bar.Resize += delegate { LayoutBar(); };

            // низ
            footer = new Panel { Dock = DockStyle.Bottom, Height = S(72) };
            btnAdd = new FlatBtn("Добавить", BtnKind.Secondary, Glyph.Add);
            btnAdd.Fit();
            btnAdd.Location = new Point(S(24), S(14));
            btnAdd.Click += delegate { ShowAddMenu(); };
            lblHint = Lbl("готовые пункты, программы из «Пуска» и запущенные — или перетащите файл в окно", "dim");
            lblHint.Location = new Point(btnAdd.Right + S(14), S(24));
            lblCount = Lbl("", "dim");
            autoToggle = new Toggle("Запуск с Windows", null, Core.AutostartEnabled());
            autoToggle.Fit();
            autoToggle.CheckedChanged += delegate { OnAutostartToggle(); };
            new ToolTip().SetToolTip(autoToggle, "При входе в Windows Старт-пак сам запустит всё из «Запустить всё» — в фоне, без окна");
            footer.Controls.AddRange(new Control[] { btnAdd, lblHint, lblCount, autoToggle });
            footer.Resize += delegate { LayoutFooter(); };

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(bar);
            Controls.Add(header);
            ResumeLayout(true);
            LayoutHeader();
            LayoutFooter();
            SyncCards();
        }

        FlatBtn IconBtn(string glyph, string tipText, EventHandler click)
        {
            var b = new FlatBtn("", BtnKind.Ghost, glyph);
            b.Fit();
            b.Size = new Size(S(44), S(44));
            b.Click += click;
            new ToolTip().SetToolTip(b, tipText);
            return b;
        }

        void LayoutHeader()
        {
            int right = header.Width - S(24);
            int y = S(26);
            foreach (FlatBtn b in new[] { btnSettings, btnMore, btnHelp })
            {
                b.Location = new Point(right - b.Width, y);
                right = b.Left - S(2);
            }
            right -= S(8);
            btnRunAll.Location = new Point(right - btnRunAll.Width, y);
            right = btnRunAll.Left - S(10);
            if (btnUpdate.Visible)
            {
                btnUpdate.Location = new Point(right - btnUpdate.Width, y);
                right = btnUpdate.Left - S(10);
            }
            lblStatus.Width = Math.Max(S(60), right - lblStatus.Left - S(8));
        }

        void LayoutBar()
        {
            int w = netBar.NeededWidth();
            netBar.Width = Math.Max(S(10), Math.Min(w + S(4), bar.Width - btnProfile.Right - S(40) - S(44)));
            netBar.Location = new Point(bar.Width - S(24) - netBar.Width, S(8));
            netBar.Invalidate();
            if (btnPing != null)
            {
                btnPing.Visible = netBar.Targets.Count > 0;
                btnPing.Enabled = !netChecking;
                btnPing.Location = new Point(netBar.Left + (netBar.Width - w) - S(4) - btnPing.Width, S(8));
            }
        }

        void LayoutFooter()
        {
            autoToggle.Location = new Point(footer.Width - S(24) - autoToggle.Width, (footer.Height - autoToggle.Height) / 2);
            lblCount.Location = new Point(autoToggle.Left - S(24) - lblCount.Width, S(24));
            lblCount.Visible = lblCount.Left > btnAdd.Right + S(12);
            lblHint.Visible = lblHint.Right < (lblCount.Visible ? lblCount.Left : autoToggle.Left) - S(12);
            PlaceAutostartTip();
        }

        // ---------------------------------------------------------------- запуск с Windows (на главном экране)

        void OnAutostartToggle()
        {
            if (syncingAuto) return;
            bool on = autoToggle.Checked;
            try { Core.SetAutostart(on); }
            catch (Exception ex) { Dlg.Info(this, "Не получилось", ex.Message); }
            SyncAutostartToggle();
            Status(Core.AutostartEnabled()
                ? "Готово: при входе в Windows всё из «Запустить всё» запустится само, в фоне." + (cfg.Tray ? " Старт-пак будет ждать у часов." : "")
                : "Запуск с Windows выключен — запускайте сами, кнопкой «Запустить всё».");
            Journal.Write("старт", "запуск с Windows " + (Core.AutostartEnabled() ? "включён" : "выключен"));
            HideAutostartTip();
        }

        void SyncAutostartToggle()
        {
            syncingAuto = true;
            autoToggle.Checked = Core.AutostartEnabled();
            syncingAuto = false;
        }

        // Первый запуск: подсветить «Запуск с Windows» и коротко объяснить.
        void MaybeShowAutostartTip(bool force = false)
        {
            if (!force && (cfg.TipAutostartShown || autoTip != null || !Visible)) return;
            if (!force && Core.AutostartEnabled()) { cfg.TipAutostartShown = true; SaveCfg(); return; }
            autoTip = new Callout("Совет: запуск с Windows",
                "Включите — и после входа в Windows zapret, прокси и остальное из «Запустить всё» запустятся сами, в фоне. Выключить можно этим же переключателем.",
                "Включить", "Не сейчас");
            autoTip.Accept += delegate { autoToggle.Checked = true; HideAutostartTip(); };
            autoTip.Dismiss += delegate { HideAutostartTip(); };
            Controls.Add(autoTip);
            autoTip.BringToFront();
            autoToggle.Glow = true;
            PlaceAutostartTip();
        }

        void PlaceAutostartTip()
        {
            if (autoTip == null) return;
            Point tg = PointToClient(autoToggle.Parent.PointToScreen(autoToggle.Location));
            int arrowX = tg.X + S(20);
            int x = Math.Max(S(12), Math.Min(ClientSize.Width - autoTip.Width - S(12), arrowX - autoTip.Width + S(40)));
            autoTip.ArrowX = arrowX - x;
            autoTip.Location = new Point(x, tg.Y - autoTip.Height - S(4));
            autoTip.Invalidate();
        }

        void HideAutostartTip()
        {
            autoToggle.Glow = false;
            if (autoTip != null)
            {
                Controls.Remove(autoTip);
                autoTip.Dispose();
                autoTip = null;
            }
            if (!cfg.TipAutostartShown) { cfg.TipAutostartShown = true; SaveCfg(); }
        }

        // ---------------------------------------------------------------- настройки: сброс, экспорт, импорт

        // Заменить настройки целиком (импорт, сброс) и сразу применить их ко всему окну.
        void ReplaceConfig(Config n, string why)
        {
            try { if (System.IO.File.Exists(Core.ConfigPath)) System.IO.File.Copy(Core.ConfigPath, Core.ConfigPath + ".bak", true); } catch { }
            n.WinX = cfg.WinX; n.WinY = cfg.WinY; n.WinW = cfg.WinW; n.WinH = cfg.WinH;
            cfg = n;
            SaveCfg();
            Theme.Apply(cfg.Theme);
            icons.Clear();
            ApplyTheme();
            ApplyTray();
            ApplyHotkey();
            ApplyOverlay();
            SyncCards();
            SyncAutostartToggle();
            RefreshState();
            Journal.Write("настройки", why);
        }

        async void RunSelfUpdateLater() { await RunSelfUpdate(); }

        void HandleSettingsRequest(string request, string file)
        {
            switch (request)
            {
                case "import":
                    try
                    {
                        Config n = Core.FromFile(file);
                        ReplaceConfig(n, "импорт из " + file);
                        Status("Настройки и список загружены из «" + System.IO.Path.GetFileName(file) + "». Прежние сохранены в StartPack.json.bak.");
                        if (Core.LastRepairs.Count > 0) Status("Загружено. Пути исправлены для этого ПК: " + string.Join(", ", Core.LastRepairs) + ".");
                    }
                    catch (Exception ex) { Dlg.Info(this, "Не получилось загрузить", ex.Message); }
                    break;
                case "reset":
                {
                    var d = new Config { Version = cfg.Version, Items = cfg.Items, Profiles = cfg.Profiles, ActiveProfile = cfg.ActiveProfile, Donate = cfg.Donate, DonateUrl = cfg.DonateUrl };
                    if (testLog == null) try { Core.SetAutostart(false); } catch { }
                    ReplaceConfig(d, "сброс настроек (список сохранён)");
                    Status("Настройки сброшены к начальным, список программ остался. Прежние — в StartPack.json.bak.");
                    break;
                }
                case "selfupdate":
                    RunSelfUpdateLater();
                    break;
                case "resetall":
                {
                    Config d = Detect.Defaults();
                    d.Donate = cfg.Donate; d.DonateUrl = cfg.DonateUrl;
                    if (testLog == null) try { Core.SetAutostart(false); } catch { }
                    ReplaceConfig(d, "полный сброс: настройки и список");
                    Status("Всё сброшено: настройки начальные, список — стандартный набор. Прежние — в StartPack.json.bak.");
                    break;
                }
            }
        }

        FlatBtn btnStandard;

        void PlaceStandardButton()
        {
            if (btnStandard == null) return;
            btnStandard.Visible = cfg.Items.Count == 0;
            btnStandard.Location = new Point((cards.Width - btnStandard.Width) / 2, Math.Min(cards.Height, S(260)) / 2 + S(56));
        }

        void SyncCards()
        {
            cards.Items = cfg.Items.ToList();
            PlaceStandardButton();
            foreach (Item gone in icons.Keys.Where(k => !cfg.Items.Contains(k)).ToList()) icons.Remove(gone);
            runEntries.Clear();
            foreach (Item it in cfg.Items)
            {
                string e = AutoStartCtl.FindEntry(it);
                if (e != null) runEntries[it] = e;
            }
            int on = cfg.Items.Count(i => i.Enabled);
            lblCount.Text = cfg.Items.Count == 0 ? "" : string.Format("{0} в списке · {1} в «Запустить всё»", cfg.Items.Count, on);
            Profile p = cfg.Active;
            btnProfile.Text = "Профиль: " + (p != null ? p.Name : "—");
            btnProfile.Fit();
            SyncNetTargets();
            LayoutFooter();
            LayoutBar();
            cards.Invalidate();
            UpdateTray();
        }

        // Состояние процессов и Happ — раз в 2 секунды (карточки рисуются из этого кэша).
        void RefreshState()
        {
            running = Core.RunningNames();
            happServerName = HappCtl.CurrentServerName();
            happGuid = HappCtl.CurrentGuid();
            int updates = cfg.Items.Count(i => i.UpdateAvailable && !i.Busy && Core.PathExists(i.Path)) + (SelfUpdate.Last != null && SelfUpdate.Last.Newer ? 1 : 0);
            if (btnUpdate.Visible != updates > 0) { btnUpdate.Visible = updates > 0; LayoutHeader(); }
            if (updates > 0 && btnUpdate.Text != "Обновить (" + updates + ")")
            {
                btnUpdate.Text = "Обновить (" + updates + ")";
                btnUpdate.Fit(); btnUpdate.Height = S(44);
                LayoutHeader();
            }
            RefreshVpn();
            Watchdog();
            int on = cfg.Items.Count(i => i.Enabled);
            lblCount.Text = cfg.Items.Count == 0 ? "" : string.Format("{0} в списке · {1} в «Запустить всё»", cfg.Items.Count, on);
            LayoutFooter();
            cards.Invalidate();
            UpdateTray();
            UpdateOverlay();
        }

        void Status(string s) { lblStatus.Text = s; }

        // ---------------------------------------------------------------- как выглядит карточка

        CardInfo Describe(Item it)
        {
            Theme t = Theme.Current;
            var ci = new CardInfo { Off = !it.Enabled, Progress = it.Progress, Tip = it.Path + (it.Args.Length > 0 ? "  " + it.Args : "") };
            bool exists = Core.PathExists(it.Path);
            bool run = Core.ProcNames(it).Length > 0 && Core.IsRunning(it, running);
            bool isVpn = VpnCtl.IsVpn(it);
            bool conn = isVpn && VpnCtl.IsConnected(it, vpn);
            var detail = new List<string>();

            if (it.Busy && it.Message != null) { ci.Dot = t.Blue; ci.Status = it.Message; }
            else if (!exists && Installers.KindOf(it) != null && !it.MessageIsError) { ci.Dot = t.TextFaint; ci.Status = "не установлена"; }
            else if (!exists) { ci.Dot = t.Red; ci.Status = "файл не найден"; }
            else if (it.Message != null && it.MessageIsError) { ci.Dot = t.Red; ci.Status = it.Message; }
            else if (isVpn)
            {
                if (run && conn) { ci.Dot = t.Green; ci.Status = "подключено"; }
                else if (run) { ci.Dot = t.Amber; ci.Status = "открыт, не подключён"; }
                else { ci.Dot = t.TextFaint; ci.Status = it.Message ?? "не запущен"; }
            }
            else if (run) { ci.Dot = t.Green; ci.Status = "работает"; }
            else if (it.Message != null) { ci.Dot = t.TextDim; ci.Status = it.Message; }
            else { ci.Dot = t.TextFaint; ci.Status = Core.ProcNames(it).Length > 0 ? "не запущена" : "—"; }

            if (it.UpdateKind.Length > 0 && exists)
            {
                string local = it.LocalVersion ?? "…";
                if (it.UpdateAvailable) detail.Add("версия " + local);
                else if (it.CheckError != null) detail.Add("версия " + local + ", " + it.CheckError);
                else if (!string.IsNullOrEmpty(it.LatestTag)) detail.Add("версия " + local + " — последняя");
                else detail.Add("версия " + local + (checking ? ", проверяю…" : ""));
                if (it.UpdateAvailable && !it.Busy && exists) ci.Badge = "↓  " + Core.CleanTag(it.LatestTag);
            }

            ci.Running = run && !it.Busy && Core.ProcNames(it).Length > 0;
            if (!exists && !it.Busy && Installers.KindOf(it) != null)
            {
                // автоматическая установка не вышла — показать и запасной путь: официальный сайт
                if (it.MessageIsError && Installers.Get(Installers.KindOf(it)).Page != null)
                    ci.Chips.Add(new KeyValuePair<string, string>("site", "С сайта"));
                ci.Chips.Add(new KeyValuePair<string, string>("install", "Установить"));
            }
            if (it.UpdateKind == "zapret" && exists && !it.Busy)
            {
                ci.Chips.Add(new KeyValuePair<string, string>("strategy", "Стратегия: " + ZapretCtl.Pretty(ZapretCtl.Current(it))));
                var foreign = ZapretCtl.Foreign(ZapretCtl.Dir(it));
                if (foreign.Count > 0 && !demo)
                {
                    detail.Add("драйвер из старой папки");
                    ci.Tip = "Из «" + string.Join("», «", foreign.Select(s => Path.GetFileName(s.Root)).Distinct()) + "» ещё работает " + ZapretCtl.Describe(foreign) + ".\n" + ci.Tip;
                    ci.Chips.Add(new KeyValuePair<string, string>("zapretsvc", "Перейти на эту папку"));
                }
            }

            if (isVpn) DescribeVpn(it, ci, detail, run, conn);

            if (it.Special == "spotify" && HostsFix.Valid(it.HostsFixDir))
            {
                string ip = string.IsNullOrEmpty(it.HostsIp) ? (HostsFix.CurrentIps().FirstOrDefault() ?? "") : it.HostsIp;
                switch (it.HostsState)
                {
                    case "checking": detail.Add("hosts: проверяю…"); break;
                    case "fixing": detail.Add("hosts: чиню…"); break;
                    case "ok": detail.Add("hosts " + ip + " отвечает"); break;
                    case "dead": detail.Add("hosts " + ip + " не отвечает"); break;
                    case "error": detail.Add("hosts: проверить не удалось"); break;
                    default: detail.Add(ip.Length > 0 ? "hosts " + ip : "в hosts пусто"); break;
                }
                if (!it.Busy && it.HostsState != "checking" && it.HostsState != "fixing")
                    ci.Chips.Add(it.HostsState == "dead"
                        ? new KeyValuePair<string, string>("fixhosts", "Починить hosts")
                        : new KeyValuePair<string, string>("checkhosts", "Проверить hosts"));
            }

            if (downloads.ContainsKey(it)) ci.Chips.Add(new KeyValuePair<string, string>("cancel", "Отменить"));
            if (it.Watch) detail.Add("автоперезапуск");
            string entry;
            if (runEntries.TryGetValue(it, out entry) && AutoStartCtl.IsEnabled(entry)) detail.Add("сама стартует с Windows");

            ci.Detail = string.Join("   ·   ", detail);
            if (it.MessageIsError && !string.IsNullOrEmpty(it.Message)) ci.Tip = it.Message + "\n\n" + it.Path;
            return ci;
        }

        Image IconOf(Item it)
        {
            Image img;
            if (icons.TryGetValue(it, out img)) return img;
            img = IconLoader.For(it, S(34) * 2);
            icons[it] = img;
            return img;
        }

        // ---------------------------------------------------------------- меню

        void ShowItemMenu(Item it, Point at)
        {
            ContextMenuStrip m = Menus.New();
            if (Describe(it).Running) Menus.Add(m.Items, "Остановить", Glyph.Stop, async delegate { await StopItem(it); });
            else Menus.Add(m.Items, "Запустить", Glyph.Play, async delegate { await RunSelected(it); });
            if (it.UpdateAvailable && !it.Busy && Core.PathExists(it.Path))
                Menus.Add(m.Items, "Обновить до " + Core.CleanTag(it.LatestTag), Glyph.Download, async delegate { await UpdateItem(it); });

            if (it.UpdateKind == "zapret")
            {
                m.Items.Add(new ToolStripSeparator());
                Menus.Add(m.Items, "Перезапустить zapret", Glyph.Refresh, async delegate { await ZapretRestart(it); });
                ToolStripMenuItem strat = Menus.Add(m.Items, "Стратегия", "", null);
                FillStrategies(strat.DropDownItems, it);
                Menus.Add(m.Items, "Подобрать стратегию тестом…", "", async delegate { await ZapretTest(it); });
            }

            if (VpnCtl.IsVpn(it))
            {
                m.Items.Add(new ToolStripSeparator());
                Menus.Add(m.Items, "Подключить", Glyph.Link, async delegate { await OnChip(it, "connect"); });
                Menus.Add(m.Items, "Отключить", Glyph.Unlink, async delegate { await OnChip(it, "disconnect"); });
                Menus.Add(m.Items, "Сервер…", Glyph.Sync, delegate { ShowVpnServers(it); });
                if (Core.IsRunning(it, running))
                    Menus.Add(m.Items, "Закрыть " + VpnCtl.Title(it.Special) + " полностью", Glyph.Stop, async delegate { await StopItem(it); });
            }

            if (it.Special == "spotify")
            {
                m.Items.Add(new ToolStripSeparator());
                if (HostsFix.Valid(it.HostsFixDir))
                {
                    Menus.Add(m.Items, "Проверить hosts", Glyph.Search, async delegate { await OnChip(it, "checkhosts"); });
                    Menus.Add(m.Items, "Починить hosts сейчас", Glyph.Shield, async delegate { await OnChip(it, "fixhosts"); });
                    bool task = HostsFix.TaskInstalled();
                    Menus.Add(m.Items, task ? "Выключить автообновление hosts" : "Включить автообновление hosts (каждые 3 ч)", Glyph.Sync,
                        async delegate { await ToggleHostsTask(it, !task); });
                    Menus.Add(m.Items, "Открыть папку SpotifyHostsFix", Glyph.Folder, delegate { try { Core.ShellOpen(Core.Expand(it.HostsFixDir)); } catch { } });
                }
                else Menus.Add(m.Items, "Указать папку SpotifyHostsFix…", Glyph.Folder, delegate { EditItem(it); });
            }

            string kindOf = Installers.KindOf(it);
            if (kindOf != null && Installers.Get(kindOf).Page != null)
            {
                m.Items.Add(new ToolStripSeparator());
                if (!Core.PathExists(it.Path)) Menus.Add(m.Items, "Установить автоматически", Glyph.Download, async delegate { await InstallApp(kindOf, it, false); });
                Menus.Add(m.Items, "Скачать с официального сайта…", Glyph.Web, delegate { Installers.OpenPage(this, Installers.Get(kindOf)); });
            }

            m.Items.Add(new ToolStripSeparator());
            string entry;
            if (runEntries.TryGetValue(it, out entry))
            {
                bool on = AutoStartCtl.IsEnabled(entry);
                Menus.Add(m.Items, on ? "Выключить её собственный автозапуск" : "Включить её собственный автозапуск", "", delegate
                {
                    AutoStartCtl.SetEnabled(entry, !on);
                    Status(on ? "«" + it.Name + "» больше не стартует сама — только из Старт-пака." : "«" + it.Name + "» снова стартует вместе с Windows.");
                    cards.Invalidate();
                });
            }
            ToolStripMenuItem watch = Menus.Add(m.Items, it.Watch ? "Не перезапускать, если закроется" : "Перезапускать, если закроется", "", delegate
            {
                it.Watch = !it.Watch;
                SaveCfg();
                Status(it.Watch ? "«" + it.Name + "» будет запускаться снова, если закроется." : "«" + it.Name + "» больше не перезапускается сама.");
                cards.Invalidate();
            });
            watch.Enabled = Core.ProcNames(it).Length > 0 && it.Special != "happ";
            Menus.Add(m.Items, "Изменить…", Glyph.Edit, delegate { EditItem(it); });
            if (!Core.IsShell(Core.Expand(it.Path))) Menus.Add(m.Items, "Открыть папку", Glyph.Folder, delegate { OpenFolder(it); });
            int idx = cfg.Items.IndexOf(it);
            ToolStripMenuItem up = Menus.Add(m.Items, "Выше", Glyph.Up, delegate { MoveItem(it, -1); });
            up.Enabled = idx > 0;
            ToolStripMenuItem down = Menus.Add(m.Items, "Ниже", Glyph.Down, delegate { MoveItem(it, 1); });
            down.Enabled = idx < cfg.Items.Count - 1;
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem del = Menus.Add(m.Items, "Убрать из списка", Glyph.Delete, delegate { DeleteItem(it); });
            del.Tag = "danger";
            del.Image = Glyph.Image(Glyph.Delete, Theme.Current.Red, 32);
            ThemeSubmenus(m.Items);
            lastMenu = m;
            m.Show(cards, at);
        }

        void FillStrategies(ToolStripItemCollection items, Item it)
        {
            string cur = ZapretCtl.Current(it);
            foreach (string bat in ZapretCtl.Strategies(it))
            {
                string b = bat;
                var mi = new ToolStripMenuItem(ZapretCtl.Pretty(bat)) { Checked = bat.Equals(cur, StringComparison.OrdinalIgnoreCase), Padding = new Padding(2, 3, 2, 3) };
                mi.Click += async delegate { await ZapretSwitch(it, b); };
                items.Add(mi);
            }
        }

        static void ThemeSubmenus(ToolStripItemCollection items)
        {
            foreach (ToolStripItem i in items)
            {
                var mi = i as ToolStripMenuItem;
                if (mi == null || mi.DropDownItems.Count == 0) continue;
                mi.DropDown.Renderer = new ThemedRenderer();
                mi.DropDown.Font = Fonts.Ui(10f);
                ThemeSubmenus(mi.DropDownItems);
            }
        }

        void ShowAddMenu()
        {
            ContextMenuStrip m = Menus.New();
            Menus.Add(m.Items, "Стандартный набор (все программы)", Glyph.Star, delegate { AddStandardSet(); });
            ToolStripMenuItem ready = Menus.Add(m.Items, "Готовые пункты", Glyph.Star, null);
            Menus.Add(ready.DropDownItems, "zapret — обход блокировок Discord и YouTube", null, async delegate { await AddTemplate("zapret"); });
            Menus.Add(ready.DropDownItems, "TG WS Proxy — прокси для Telegram", null, async delegate { await AddTemplate("tgws"); });
            Menus.Add(ready.DropDownItems, "Spotify + починка hosts", null, async delegate { await AddTemplate("spotify"); });
            Menus.Add(ready.DropDownItems, "Telegram", null, async delegate { await AddTemplate("telegram"); });
            Menus.Add(ready.DropDownItems, "Discord", null, async delegate { await AddTemplate("discord"); });
            ready.DropDownItems.Add(new ToolStripSeparator());
            Menus.Add(ready.DropDownItems, "Happ — VPN с автоподключением", null, async delegate { await AddTemplate("happ"); });
            Menus.Add(ready.DropDownItems, "INCY — VPN с автоподключением", null, async delegate { await AddTemplate("incy"); });
            Menus.Add(ready.DropDownItems, "Koala Clash — VPN, сервер по номеру строки", null, async delegate { await AddTemplate("koala"); });
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m.Items, "Из запущенных программ…", Glyph.Process, delegate { AddFromRunning(); });
            Menus.Add(m.Items, "Из меню «Пуск»…", Glyph.Store, delegate { AddFromStart(); });
            Menus.Add(m.Items, "Файл или ярлык…", Glyph.File, delegate { AddFromFile(); });
            Menus.Add(m.Items, "Вставить свой путь…", Glyph.Keyboard, delegate { AddViaEditor(new Item(), "Добавить программу"); });
            ThemeSubmenus(m.Items);
            lastMenu = m;
            m.Show(btnAdd, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }

        void AddStandardSet()
        {
            List<Item> added = Templates.AddStandard(cfg);
            if (added.Count == 0) { Status("Все программы стандартного набора уже в списке."); return; }
            SaveCfg();
            SyncCards();
            RefreshState();
            var missing = added.Where(x => !Core.PathExists(x.Path)).Select(x => x.Name).ToList();
            Status("Добавлено: " + string.Join(", ", added.Select(x => x.Name)) + "." +
                (missing.Count > 0 ? " Не установлены: " + string.Join(", ", missing) + " — кнопка «Установить» на карточке." : "") +
                (added.Any(VpnCtl.IsVpn) ? " VPN добавлены выключенными." : ""));
            Journal.Write("список", "стандартный набор: добавлено " + string.Join(", ", added.Select(x => x.Name)));
        }

        void ShowMoreMenu()
        {
            ContextMenuStrip m = Menus.New();
            if (SelfUpdate.Last != null && SelfUpdate.Last.Newer)
            {
                ToolStripMenuItem su = Menus.Add(m.Items, "Обновить Старт-пак до " + SelfUpdate.Last.Version, Glyph.Download, async delegate { await RunSelfUpdate(); });
                su.Font = Fonts.Semi(10f);
                m.Items.Add(new ToolStripSeparator());
            }
            Menus.Add(m.Items, "Журнал и отчёт…", "", delegate { ShowJournal(); });
            Menus.Add(m.Items, "Установить программы…", Glyph.Download, delegate { ShowInstalls(); });
            Menus.Add(m.Items, "Найти программы заново", Glyph.Search, delegate { FindAgain(); });
            Menus.Add(m.Items, "Уборка…", "", delegate { ShowCleanup(); });
            Menus.Add(m.Items, "Проверить связь", Glyph.Globe, async delegate { await CheckNet(true); });
            Menus.Add(m.Items, (cfg.Overlay ? "Скрыть оверлей" : "Показать оверлей") + "    Ctrl+Alt+O", "\uE7F4", delegate { ToggleOverlay(); });
            Menus.Add(m.Items, "Настроить оверлей…", "", delegate { ShowOverlaySettings(); });
            Menus.Add(m.Items, "Проверить обновления", Glyph.Refresh, async delegate { await CheckUpdates(true); });
            Menus.Add(m.Items, "Справка", "", delegate { ShowHelp(); });
            Menus.Add(m.Items, "Страница проекта на GitHub", Glyph.Web, delegate { try { Core.ShellOpen(SelfUpdate.Page); } catch { } });
            if (Core.HasDonate(cfg))
                Menus.Add(m.Items, "Поблагодарить автора", Glyph.Heart, delegate { ShowDonate(); });
            if (cfg.Tray)
            {
                m.Items.Add(new ToolStripSeparator());
                Menus.Add(m.Items, "Выйти из Старт-пака", "", delegate { ExitApp(); });
            }
            lastMenu = m;
            m.Show(btnMore, new Point(btnMore.Width - S(260), btnMore.Height));
        }

        void OpenSettings()
        {
            string request, file;
            using (var f = new SettingsForm(cfg, () => { ApplyTheme(); SaveCfg(); }, () => { ApplyTray(); ApplyHotkey(); ApplyOverlay(); SaveCfg(); }))
            {
                f.ShowDialog(this);
                request = f.Request;
                file = f.RequestFile;
            }
            SaveCfg();
            SyncAutostartToggle();
            HandleSettingsRequest(request, file);
            if (request == "uninstall")
            {
                Journal.Write("старт", "удаление Старт-пака");
                string kept = Setup.Uninstall();
                Core.Frozen = true;   // папка вот-вот удалится — больше ничего не записывать
                Dlg.Info(this, "Старт-пак удалён", kept != null ? "Скачанное Старт-паком (например, TG WS Proxy) осталось в папке:\n" + kept : "Окно сейчас закроется.");
                exiting = true;
                Close();
            }
            else if (request == "install")
            {
                try
                {
                    Setup.Install(true);
                    // новый экземпляр стартует, когда этот закроется (иначе он просто покажет это окно)
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c \"timeout /t 2 /nobreak >nul & start \"\" \"" + Setup.InstalledExe + "\"\"")
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Setup.InstallDir });
                    exiting = true;
                    Close();
                }
                catch (Exception ex) { Dlg.Info(this, "Не получилось установить", ex.Message); }
            }
        }

        public void ApplyTheme()
        {
            Restyle(this);
            cards.Invalidate();
            netBar.Invalidate();
            UpdateTray(true);
        }

        // Заново найти программы, у которых пропал файл (переустановили, перенесли, список с другого ПК).
        void FindAgain()
        {
            Cursor = Cursors.WaitCursor;
            Core.ResetShellCache();
            var fixes = Core.Repair(cfg);
            foreach (Item it in cfg.Items) if (Core.AdoptNewestZapret(it) && !fixes.Contains(it.Name)) fixes.Add(it.Name);
            Cursor = Cursors.Default;
            if (fixes.Count > 0) { SaveCfg(); icons.Clear(); }
            SyncCards();
            var still = cfg.Items.Where(i => !Core.PathExists(i.Path)).Select(i => i.Name).ToList();
            Status((fixes.Count > 0 ? "Обновил пути: " + string.Join(", ", fixes) + ". " : "Новых путей не нашлось. ") +
                (still.Count > 0 ? "Не найдены: " + string.Join(", ", still) + " — их можно установить кнопкой на карточке." : "Все программы на месте."));
            Journal.Write("поиск", "найти заново: обновлено " + fixes.Count + ", не найдено " + still.Count);
        }

        void ShowDonate()
        {
            using (var f = new DonateForm(cfg)) f.ShowDialog(this);
        }

        void ShowHelp()
        {
            using (var f = new HelpForm()) f.ShowDialog(this);
        }

        void ShowJournal()
        {
            using (var f = new JournalForm(() => Journal.Report(cfg, Core.RunningNames(), NetSummary()))) f.ShowDialog(this);
        }

        void ShowCleanup()
        {
            using (var f = new CleanupForm(cfg, testLog != null, x => MarkExpected(x, true)))
            {
                f.ShowDialog(this);
                if (f.Changed) { SaveCfg(); icons.Clear(); SyncCards(); }
            }
        }

        // ---------------------------------------------------------------- события

        bool startedOnce;

        async void Startup()
        {
            if (startedOnce) return;   // окно, открытое из трея, не должно запускать всё ещё раз
            startedOnce = true;
            if (loadWarning != null) Dlg.Info(this, "Список собран заново", loadWarning);
            if (testLog != null) { await RunTest(); return; }
            if (autorun)
            {
                if (cfg.AutorunDelay > 0)
                {
                    // дать сети и VPN подняться после входа в Windows
                    for (int left = cfg.AutorunDelay; left > 0; left--)
                    {
                        Status("Запущу всё через " + left + " с…");
                        await Task.Delay(1000);
                    }
                }
                await RunAllCore();
                if (cfg.CheckUpdatesOnStart) { await CheckUpdates(false); NotifyUpdates(); }
                TryAutoClose();
                return;
            }
            Status("Нажмите «Запустить всё» — или запускайте по одной кнопкой ▶ на карточке.");
            MaybeShowAutostartTip();
            if (Core.LastRepairs.Count > 0)
                Status("Нашёл заново и обновил пути: " + string.Join(", ", Core.LastRepairs) + ".");
            if (Core.FirstRun)
            {
                var missing = Installers.All.Where(a => Installers.SafeDetect(a) == null).Select(a => a.Name).ToList();
                if (missing.Count > 0) Status("Не установлены: " + string.Join(", ", missing) + " — кнопка «Установить» на карточке или «С сайта» в «⋯».");
            }
            if (SelfUpdate.JustUpdated) Status("Старт-пак обновлён до " + Core.Version + " — список программ и настройки на месте.");
            if (cfg.CheckUpdatesOnStart) { await CheckSelfUpdate(false); await CheckUpdates(false); }
            if (cfg.NetCheck) await CheckNet(false);
            await CheckAllHosts();
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            if (files.Length == 1)
            {
                string f = files[0];
                BeginInvoke((Action)(() => AddViaEditor(NewItemFor(f, Path.GetFileNameWithoutExtension(f)), "Добавить программу")));
                return;
            }
            foreach (string f in files) cfg.Items.Add(NewItemFor(f, Path.GetFileNameWithoutExtension(f)));
            cfg.StoreProfile();
            SaveCfg();
            SyncCards();
            Status("Добавлено: " + files.Length + ". Двойной щелчок по карточке — изменить.");
        }

        static Item NewItemFor(string path, string name)
        {
            string special = Core.GuessSpecial(path);
            var it = new Item { Name = name, Path = path, ProcessName = Core.GuessProcess(path, ""), Special = special };
            if (special == "happ") it.HappAutoConnect = true;
            if (special == "spotify") { it.SpotifyCheckHosts = true; it.HostsFixDir = HostsFix.Find() ?? ""; }
            return it;
        }

        // ---------------------------------------------------------------- правка списка

        void AddFromFile()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Что запускать", DereferenceLinks = false,
                Filter = "Программы, скрипты и ярлыки|*.exe;*.bat;*.cmd;*.lnk;*.ps1;*.url|Все файлы|*.*"
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                AddViaEditor(NewItemFor(d.FileName, Path.GetFileNameWithoutExtension(d.FileName)), "Добавить программу");
            }
        }

        void AddFromStart()
        {
            using (var f = PickerForm.StartMenu())
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Picked == null) return;
                string path = Core.StartAppToPath(f.Picked.Value);
                AddViaEditor(NewItemFor(path, f.Picked.Title), "Добавить программу");
            }
        }

        void AddFromRunning()
        {
            using (var f = PickerForm.Running())
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Picked == null) return;
                var app = (Core.RunningApp)f.Picked.Tag;
                Item it = NewItemFor(app.Path, app.Title);
                it.ProcessName = app.Name;
                AddViaEditor(it, "Добавить программу");
            }
        }

        static bool SameKind(Item a, Item b)
        {
            if (a.Special.Length > 0 && a.Special == b.Special) return true;
            if (a.Repo.Length > 0 && string.Equals(a.Repo, b.Repo, StringComparison.OrdinalIgnoreCase)) return true;
            return a.ProcessName.Length > 0 && string.Equals(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        void AddViaEditor(Item it, string title)
        {
            using (var f = new ItemForm(it, title))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                cfg.Items.Add(f.Result);
                VpnCtl.SortLast(cfg);
                cfg.StoreProfile();
                SaveCfg();
                SyncCards();
                cards.ScrollTo(cfg.Items.IndexOf(f.Result));
                Journal.Write("список", "добавлена «" + f.Result.Name + "»: " + f.Result.Path);
                if (f.Result.UpdateKind.Length > 0) { var _ = CheckUpdates(false); }
                if (f.Result.Special == "spotify") { var __ = CheckHosts(f.Result, false); }
            }
        }

        void EditItem(Item it)
        {
            int i = cfg.Items.IndexOf(it);
            if (i < 0) return;
            using (var f = new ItemForm(it, "Изменить: " + it.Name))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                cfg.Items[i] = f.Result;
                icons.Remove(it);
                Core.AdoptNewestZapret(f.Result);
                cfg.StoreProfile();
                SaveCfg();
                SyncCards();
                Journal.Write("список", "изменена «" + f.Result.Name + "»");
                if (f.Result.UpdateKind.Length > 0 && f.Result.LatestTag == null) { var _ = CheckUpdates(false); }
            }
        }

        void DeleteItem(Item it)
        {
            if (!Ask("Убрать из списка?", "«" + it.Name + "» пропадёт из Старт-пака. Сама программа останется на компьютере.", "Убрать", "Отмена")) return;
            cfg.Items.Remove(it);
            foreach (Profile p in cfg.Profiles) p.Ids.Remove(it.Id);
            SaveCfg();
            SyncCards();
            Journal.Write("список", "убрана «" + it.Name + "»");
        }

        void MoveItem(Item it, int dir)
        {
            int i = cfg.Items.IndexOf(it), j = i + dir;
            if (i < 0 || j < 0 || j >= cfg.Items.Count) return;
            cfg.Items[i] = cfg.Items[j];
            cfg.Items[j] = it;
            SaveCfg();
            SyncCards();
        }

        void OpenFolder(Item it)
        {
            string p = Core.Expand(it.Path);
            try
            {
                if (File.Exists(p)) Process.Start("explorer.exe", "/select,\"" + p + "\"");
                else
                {
                    string d = Core.SafeDir(p);
                    if (d != null && Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\"");
                }
            }
            catch { }
        }

        void SaveCfg()
        {
            try { Core.Save(cfg); }
            catch (Exception ex) { Journal.Write("ошибка", "не удалось сохранить список: " + ex.Message); Dlg.Info(this, "Не удалось сохранить список", ex.Message); }
        }

        // Сообщение; в самопроверке — только в лог (окно остановило бы проверку).
        void Tell(string title, string text)
        {
            if (testLog != null) { Log("СООБЩЕНИЕ: " + title + " — " + text.Replace("\n", " ")); return; }
            Dlg.Info(this, title, text);
        }

        bool Ask(string title, string q, string yes, string no)
        {
            if (testLog != null)
            {
                bool sayYes = Env("STARTPACK_TEST_YES");
                Log("ВОПРОС (ответ «" + (sayYes ? "да" : "нет") + "»): " + title + " — " + q.Replace("\n", " "));
                return sayYes;
            }
            if (!Visible) ShowFromTray();
            return Dlg.Ask(this, title, q, yes, no);
        }

        void SetMsg(Item it, string msg, bool error)
        {
            it.Message = msg;
            it.MessageIsError = error;
            cards.Invalidate();
        }

        // ---------------------------------------------------------------- запуск

        async void RunAll()
        {
            await RunAllCore();
            TryAutoClose();
        }

        async Task RunAllCore()
        {
            if (runningAll) return;
            runningAll = true;
            stopRun = false;
            btnRunAll.Text = "Остановить";
            btnRunAll.Kind = BtnKind.Secondary;
            btnRunAll.Icon = "\uE71A";
            btnRunAll.Fit(); btnRunAll.Height = S(44); btnRunAll.Width += S(10);
            LayoutHeader();
            int started = 0, already = 0, errors = 0;
            Profile prof = cfg.Active;
            Journal.Write("запуск", "«Запустить всё», профиль «" + (prof != null ? prof.Name : "-") + "»");
            try
            {
                foreach (Item it in cfg.Items.ToList())
                {
                    if (!it.Enabled) continue;
                    if (stopRun) { Journal.Write("запуск", "остановлено пользователем перед «" + it.Name + "»"); break; }
                    int r = await RunOne(it, true);
                    if (r == 0) started++; else if (r == 1) already++; else errors++;
                }
            }
            finally
            {
                runningAll = false;
                btnRunAll.Text = "Запустить всё";
                btnRunAll.Kind = BtnKind.Primary;
                btnRunAll.Icon = Glyph.Play;
                btnRunAll.Fit(); btnRunAll.Height = S(44); btnRunAll.Width += S(10);
                LayoutHeader();
            }
            lastErrors = errors;
            var parts = new List<string>();
            if (started > 0) parts.Add("запущено: " + started);
            if (already > 0) parts.Add("уже работали: " + already);
            if (errors > 0) parts.Add("ошибок: " + errors);
            if (stopRun) parts.Add("остановлено вами");
            string summary = parts.Count == 0 ? "В «Запустить всё» нет ни одной программы — включите переключатели на карточках." : "Готово — " + string.Join(", ", parts) + ".";
            Status(summary);
            Journal.Write("запуск", summary);
            if (errors > 0 && !Visible) Notify("Не всё запустилось", summary + " Нажмите, чтобы открыть Старт-пак.", ShowFromTray);
            await Task.Delay(1500);
            RefreshState();
            if (cfg.NetCheck && (started > 0 || errors > 0))
            {
                await Task.Delay(2500);
                await CheckNet(false);
            }
        }

        void TryAutoClose()
        {
            if (testLog != null) return;
            if (lastErrors == 0 && cfg.CloseAfterLaunch && !checking && !cfg.Items.Any(i => i.UpdateAvailable || i.Busy))
            {
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += delegate
                {
                    t.Stop();
                    if (cfg.Tray) HideToTray(); else Close();
                };
                t.Start();
            }
        }

        async Task RunSelected(Item it)
        {
            int r = await RunOne(it, false);
            Status(r == 0 ? "«" + it.Name + "» запущена." : r == 1 ? "«" + it.Name + "» уже работает." : "«" + it.Name + "»: не получилось — подробности на карточке.");
            await Task.Delay(1500);
            RefreshState();
        }

        // 0 — запущено, 1 — уже работало, 2 — ошибка
        async Task<int> RunOne(Item it, bool inSequence)
        {
            if (Core.AdoptNewestZapret(it)) SaveCfg();
            if (!Core.PathExists(it.Path)) { SetMsg(it, "файл не найден", true); Journal.Write("запуск", it.Name + ": файл не найден — " + it.Path); return 2; }
            running = Core.RunningNames();
            if (VpnCtl.IsVpn(it)) return await RunVpn(it, inSequence);
            if (it.SkipIfRunning && Core.IsRunning(it, running)) { SetMsg(it, null, false); Journal.Write("запуск", it.Name + ": уже работает"); return 1; }
            if (it.Special == "spotify") await PrepareSpotify(it);
            int r = TryLaunch(it);
            if (r != 0) return r;
            if (inSequence && it.WaitReady && Core.ProcNames(it).Length > 0)
            {
                it.Busy = true;
                SetMsg(it, "жду, пока запустится…", false);
                bool ready = await HappCtl.WaitFor(() => Core.IsRunning(it, Core.RunningNames()), it.Admin ? 45000 : 20000);
                it.Busy = false;
                SetMsg(it, ready ? "запущена" : "запущена, но процесс не появился", false);
                Journal.Write("запуск", it.Name + (ready ? ": готова" : ": процесс так и не появился (" + it.ProcessName + ")"));
            }
            if (inSequence && it.Delay > 0) await Task.Delay(it.Delay * 1000);
            return 0;
        }

        int TryLaunch(Item it)
        {
            SetMsg(it, "запускаю…", false);
            try
            {
                MarkExpected(it, false);
                Core.Launch(it);
            }
            catch (Win32Exception ex)
            {
                string msg = ex.NativeErrorCode == 1223 ? "отменено — не дали права администратора" : ex.Message;
                SetMsg(it, msg, true);
                Journal.Write("запуск", it.Name + ": " + msg);
                return 2;
            }
            catch (Exception ex)
            {
                SetMsg(it, ex.Message, true);
                Journal.Write("запуск", it.Name + ": ошибка — " + ex.Message);
                return 2;
            }
            SetMsg(it, "запущена", false);
            Journal.Write("запуск", it.Name + ": запущена");
            return 0;
        }
    }
}
