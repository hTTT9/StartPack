// Окна: добавить/изменить программу, выбор из «Пуска» и из запущенных, настройки.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    // ---------------------------------------------------------------- добавить / изменить

    public class ItemForm : ThemedForm
    {
        public Item Result;
        readonly Item src;
        readonly TableLayoutPanel t;
        int row;
        readonly List<HappPreset> presets;

        TextField tName, tPath, tArgs, tProc, tRepo, tAsset, tHostsDir;
        NumericUpDown nDelay;
        Toggle cAdmin, cSkip, cHappAuto, cSpotifyCheck, cWatch, cWait, cOwnAuto;
        readonly string autoEntry;
        DarkCombo cbType, cbUpd, cbServer;
        Label lHappNow, lHostsState;
        readonly List<Control> happRows = new List<Control>(), spotifyRows = new List<Control>(), updRows = new List<Control>(),
            vpnRows = new List<Control>(), koalaRows = new List<Control>(), incyRows = new List<Control>();
        NumericUpDown nKoalaServer;

        static readonly string[] Types = { "", "happ", "spotify", "incy", "koala" };
        static readonly string[] Kinds = { "", "zapret", "github-exe" };

        int FieldW { get { return S(470); } }

        public ItemForm(Item it, string title)
        {
            src = it;
            presets = it.HappPresets.Select(p => p.Copy()).ToList();
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Padding = new Padding(S(24), S(16), S(24), S(20)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(t);

            Section("ОСНОВНОЕ", null);
            tName = Field("Название", it.Name, null, null);
            var browse = new FlatBtn("Обзор", BtnKind.Secondary, null) { Compact = true };
            browse.Fit();
            browse.Click += Browse;
            tPath = Field("Что запускать", it.Path, browse, null);
            Hint("Программа (.exe), .bat, ярлык (.lnk), скрипт .ps1 или приложение из Microsoft Store (shell:AppsFolder\\…). Можно вставить любой путь.", null);
            tArgs = Field("Параметры", it.Args, null, null);
            tProc = Field("Процесс", it.ProcessName, null, null);
            Hint("Имя процесса без .exe (можно несколько через запятую) — по нему видно, что программа уже работает.", null);
            nDelay = new NumericUpDown { Minimum = 0, Maximum = 120, Value = Math.Min(120, Math.Max(0, it.Delay)), Width = S(80), BorderStyle = BorderStyle.FixedSingle, Font = Fonts.Ui(10f) };
            nDelay.BackColor = Theme.Current.Surface; nDelay.ForeColor = Theme.Current.Text;
            Row("Пауза после запуска, сек", nDelay, null);
            cAdmin = new Toggle("От имени администратора", "Windows спросит разрешение при запуске", it.Admin);
            Full(cAdmin, null);
            cSkip = new Toggle("Не запускать второй раз, если уже работает", null, it.SkipIfRunning);
            Full(cSkip, null);
            cWait = new Toggle("Ждать, пока она запустится", "В «Запустить всё» следующая программа стартует только после этой — например, Discord после zapret.", it.WaitReady);
            Full(cWait, null);
            cWatch = new Toggle("Перезапускать, если закроется", "Если программа закроется или упадёт, Старт-пак сразу запустит её снова. Если нужны права администратора — предложит в уведомлении.", it.Watch);
            Full(cWatch, null);
            autoEntry = AutoStartCtl.FindEntry(it);
            if (autoEntry != null)
            {
                cOwnAuto = new Toggle("Её собственный автозапуск при входе в Windows",
                    "Выключите, чтобы она стартовала только из Старт-пака и в нужном порядке. Это тот же переключатель, что в «Диспетчер задач → Автозагрузка».",
                    AutoStartCtl.IsEnabled(autoEntry));
                Full(cOwnAuto, null);
            }

            Section("ОСОБЫЕ ВОЗМОЖНОСТИ", null);
            cbType = new DarkCombo { Width = FieldW };
            cbType.Items.AddRange(new object[] { "Нет — обычная программа", "Happ — VPN: подключение, сохранённые серверы", "Spotify — проверка и починка hosts",
                "INCY — VPN: подключение", "Koala Clash — VPN: подключение, сервер по номеру строки" });
            cbType.SelectedIndex = Math.Max(0, Array.IndexOf(Types, it.Special ?? ""));
            cbType.SelectedIndexChanged += delegate { UpdateVisibility(); };
            Row("Тип", cbType, null);

            // общее для VPN
            cHappAuto = new Toggle("Подключаться сразу после запуска", "Если клиент уже открыт, Старт-пак только подключит его — без перезапуска.", it.HappAutoConnect);
            Full(cHappAuto, vpnRows);

            // Koala: сервер по номеру строки
            nKoalaServer = new NumericUpDown { Minimum = 0, Maximum = 999, Value = Math.Min(999, Math.Max(0, it.VpnServer)), Width = S(80), BorderStyle = BorderStyle.FixedSingle, Font = Fonts.Ui(10f) };
            nKoalaServer.BackColor = Theme.Current.Surface; nKoalaServer.ForeColor = Theme.Current.Text;
            Row("Сервер №", nKoalaServer, null, koalaRows);
            Hint("Номер строки в списке серверов Koala: 1 — первая, 2 — вторая… 0 — как выбрано в самой Koala. Список с названиями — кнопка «Сервер» на карточке.", koalaRows);
            Hint("INCY подключается к серверу, выбранному в самом INCY: выбрать строку снаружи INCY пока не позволяет.", incyRows);
            cbServer = new DarkCombo { Width = FieldW };
            var delPreset = new FlatBtn("Удалить", BtnKind.Ghost, null) { Compact = true };
            delPreset.Fit();
            delPreset.Click += delegate
            {
                if (cbServer.SelectedIndex <= 0) return;
                presets.RemoveAt(cbServer.SelectedIndex - 1);
                FillServers("");
            };
            Row("Сервер", cbServer, delPreset, happRows);
            var capture = new FlatBtn("Запомнить сервер, выбранный в Happ", BtnKind.Secondary, Glyph.Save) { Compact = true };
            capture.Fit();
            capture.Click += delegate { CaptureServer(); };
            Full(capture, happRows);
            lHappNow = Hint("", happRows);
            Hint("Сохранённый сервер подставляется, когда Happ запускается из Старт-пака. Сменить сервер у уже открытого Happ можно в меню карточки «⋯».", happRows);
            FillServers(it.HappServer);

            // Spotify
            var browseDir = new FlatBtn("Обзор", BtnKind.Secondary, null) { Compact = true };
            browseDir.Fit();
            browseDir.Click += BrowseHostsDir;
            tHostsDir = Field("Папка SpotifyHostsFix", it.HostsFixDir, browseDir, spotifyRows);
            tHostsDir.Box.TextChanged += delegate { UpdateHostsState(); };
            lHostsState = Hint("", spotifyRows);
            cSpotifyCheck = new Toggle("Проверять hosts перед запуском Spotify",
                "Проверка — около 5 секунд, без прав администратора. Если адрес умер, Старт-пак починит hosts (Windows спросит разрешение).", it.SpotifyCheckHosts);
            Full(cSpotifyCheck, spotifyRows);

            Section("ОБНОВЛЕНИЯ С GITHUB", null);
            cbUpd = new DarkCombo { Width = FieldW };
            cbUpd.Items.AddRange(new object[]
            {
                "Нет",
                "zapret-discord-youtube: новая папка рядом, списки переносятся",
                "Один .exe из релизов GitHub: файл заменяется новым"
            });
            cbUpd.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, it.UpdateKind ?? ""));
            cbUpd.SelectedIndexChanged += OnKindChanged;
            Row("Обновления", cbUpd, null);
            tRepo = Field("Репозиторий", it.Repo, null, updRows);
            Hint("Вида «владелец/название», например Flowseal/tg-ws-proxy", updRows);
            tAsset = Field("Файл в релизе", it.Asset, null, updRows);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(18), 0, 0) };
            var cancel = new FlatBtn("Отмена", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            cancel.Fit();
            var ok = new FlatBtn("Сохранить", BtnKind.Primary, null) { Margin = new Padding(0, 3, S(10), 3) };
            ok.Fit();
            ok.Click += OnOk;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            t.Controls.Add(buttons, 0, row);
            t.SetColumnSpan(buttons, 3);
            row++;
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateHostsState();
            UpdateVisibility();
        }

        // ---------- построение строк

        void Section(string text, List<Control> group)
        {
            Label l = Lbl(text, "section");
            l.Margin = new Padding(0, row == 0 ? 0 : S(18), 0, S(6));
            t.Controls.Add(l, 0, row);
            t.SetColumnSpan(l, 3);
            if (group != null) group.Add(l);
            row++;
        }

        TextField Field(string label, string value, Control extra, List<Control> group)
        {
            var tf = new TextField(value, FieldW) { Margin = new Padding(0, S(4), 0, S(4)) };
            Row(label, tf, extra, group);
            return tf;
        }

        void Row(string label, Control c, Control extra, List<Control> group = null)
        {
            Label l = Lbl(label, "");
            l.Margin = new Padding(0, S(11), S(14), 0);
            t.Controls.Add(l, 0, row);
            if (c.Margin == new Padding(3)) c.Margin = new Padding(0, S(5), 0, S(5));
            t.Controls.Add(c, 1, row);
            if (group != null) { group.Add(l); group.Add(c); }
            if (extra != null)
            {
                extra.Margin = new Padding(S(8), S(4), 0, 0);
                t.Controls.Add(extra, 2, row);
                if (group != null) group.Add(extra);
            }
            row++;
        }

        void Full(Control c, List<Control> group)
        {
            Toggle tg = c as Toggle;
            if (tg != null) tg.Fit();
            c.Margin = new Padding(0, S(6), 0, S(6));
            t.Controls.Add(c, 1, row);
            t.SetColumnSpan(c, 2);
            if (group != null) group.Add(c);
            row++;
        }

        Label Hint(string text, List<Control> group)
        {
            Label l = Lbl(text, "dim");
            l.MaximumSize = new Size(FieldW + S(90), 0);
            l.Margin = new Padding(0, 0, 0, S(6));
            t.Controls.Add(l, 1, row);
            t.SetColumnSpan(l, 2);
            if (group != null) group.Add(l);
            row++;
            return l;
        }

        // ---------- логика

        string Special() { return Types[Math.Max(0, cbType.SelectedIndex)]; }
        string Kind() { return Kinds[Math.Max(0, cbUpd.SelectedIndex)]; }

        void UpdateVisibility()
        {
            SuspendLayout();
            string sp = Special();
            foreach (Control c in happRows) c.Visible = sp == "happ";
            foreach (Control c in spotifyRows) c.Visible = sp == "spotify";
            foreach (Control c in vpnRows) c.Visible = sp == "happ" || sp == "incy" || sp == "koala";
            foreach (Control c in koalaRows) c.Visible = sp == "koala";
            foreach (Control c in incyRows) c.Visible = sp == "incy";
            if (sp == "incy" && tProc.Text.Trim().Length == 0) tProc.Text = "incy";
            if (sp == "koala" && tProc.Text.Trim().Length == 0) tProc.Text = "Koala Clash";
            foreach (Control c in updRows) c.Visible = Kind().Length > 0;
            tAsset.Enabled = Kind() == "github-exe";
            if (sp == "happ")
            {
                string now = HappCtl.CurrentServerName();
                lHappNow.Text = string.IsNullOrEmpty(now) ? "В Happ сейчас не выбран сервер." : "В Happ сейчас выбран: " + now;
                if (tProc.Text.Trim().Length == 0) tProc.Text = "Happ";
            }
            if (sp == "spotify" && tHostsDir.Text.Trim().Length == 0) tHostsDir.Text = HostsFix.Find() ?? "";
            ResumeLayout(true);
        }

        void FillServers(string select)
        {
            cbServer.Items.Clear();
            cbServer.Items.Add("Последний выбранный в самом Happ");
            foreach (HappPreset p in presets) cbServer.Items.Add(p.Name);
            int i = presets.FindIndex(p => p.Name == select);
            cbServer.SelectedIndex = i >= 0 ? i + 1 : 0;
        }

        void CaptureServer()
        {
            HappPreset p = HappCtl.Capture();
            if (p == null)
            {
                Dlg.Info(this, "Сервер не найден", "В настройках Happ нет выбранного сервера. Выберите или подключитесь к нужному серверу в Happ и нажмите ещё раз.");
                return;
            }
            HappPreset same = presets.FirstOrDefault(x => x.Guid == p.Guid);
            if (same != null) { FillServers(same.Name); return; }
            string name = p.Name;
            for (int n = 2; presets.Any(x => x.Name == p.Name); n++) p.Name = name + " (" + n + ")";
            presets.Add(p);
            FillServers(p.Name);
        }

        void UpdateHostsState()
        {
            string d = tHostsDir.Text.Trim();
            if (d.Length == 0) lHostsState.Text = "Папка со скриптом Spotify-Hosts-Update.ps1. Без неё Spotify просто запускается.";
            else if (HostsFix.Valid(d))
            {
                var ips = HostsFix.CurrentIps();
                lHostsState.Text = "Скрипт найден. Сейчас в hosts: " + (ips.Count > 0 ? string.Join(", ", ips) : "адресов Spotify нет") + ".";
            }
            else lHostsState.Text = "В этой папке нет Spotify-Hosts-Update.ps1.";
        }

        void OnKindChanged(object sender, EventArgs e)
        {
            string k = Kind();
            if (k == "zapret")
            {
                if (tRepo.Text.Trim().Length == 0) tRepo.Text = "Flowseal/zapret-discord-youtube";
                if (tProc.Text.Trim().Length == 0) tProc.Text = "winws";
                cAdmin.Checked = true;
            }
            else if (k == "github-exe" && tAsset.Text.Trim().Length == 0)
            {
                try { tAsset.Text = Path.GetFileName(Core.Expand(tPath.Text)); } catch { }
            }
            UpdateVisibility();
        }

        void Browse(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog
            {
                Title = "Что запускать", DereferenceLinks = false,
                Filter = "Программы, скрипты и ярлыки|*.exe;*.bat;*.cmd;*.lnk;*.ps1;*.url|Все файлы|*.*"
            })
            {
                try
                {
                    string cur = Core.Expand(tPath.Text);
                    if (cur.Length > 0 && !Core.IsShell(cur) && File.Exists(cur)) d.InitialDirectory = Path.GetDirectoryName(cur);
                }
                catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                tPath.Text = d.FileName;
                if (tName.Text.Trim().Length == 0) tName.Text = Path.GetFileNameWithoutExtension(d.FileName);
                if (tProc.Text.Trim().Length == 0) tProc.Text = Core.GuessProcess(d.FileName, Kind());
                string sp = Core.GuessSpecial(d.FileName);
                if (sp.Length > 0 && Special().Length == 0) cbType.SelectedIndex = Array.IndexOf(Types, sp);
            }
        }

        void BrowseHostsDir(object sender, EventArgs e)
        {
            using (var d = new FolderBrowserDialog { Description = "Папка SpotifyHostsFix (там, где Spotify-Hosts-Update.ps1)", ShowNewFolderButton = false })
            {
                string cur = Core.Expand(tHostsDir.Text);
                if (Directory.Exists(cur)) d.SelectedPath = cur;
                if (d.ShowDialog(this) == DialogResult.OK) tHostsDir.Text = d.SelectedPath;
            }
        }

        void OnOk(object sender, EventArgs e)
        {
            if (tName.Text.Trim().Length == 0 || tPath.Text.Trim().Length == 0)
            {
                Dlg.Info(this, "Не хватает данных", "Заполните название и что запускать.");
                return;
            }
            string kind = Kind(), sp = Special();
            if (kind.Length > 0 && !Regex.IsMatch(tRepo.Text.Trim(), @"^[\w.-]+/[\w.-]+$"))
            {
                Dlg.Info(this, "Репозиторий", "Укажите репозиторий GitHub вида «владелец/название» — или выключите обновления.");
                return;
            }
            string path = tPath.Text.Trim().Trim('"');
            if (!Core.PathExists(path) && !Dlg.Ask(this, "Файл не найден", path + "\n\nВсё равно сохранить?", "Сохранить", "Исправить")) return;
            if (sp == "spotify" && cSpotifyCheck.Checked && !HostsFix.Valid(tHostsDir.Text) &&
                !Dlg.Ask(this, "Нет скрипта починки", "В указанной папке нет Spotify-Hosts-Update.ps1 — проверка hosts работать не будет. Сохранить так?", "Сохранить", "Исправить")) return;

            Item r = src.Clone();
            bool updChanged = r.UpdateKind != kind || r.Repo != tRepo.Text.Trim() || r.Asset != tAsset.Text.Trim() || r.Path != path;
            if (r.Path != path) { r.InstalledTag = ""; r.InstalledSize = 0; }
            r.Name = tName.Text.Trim();
            r.Path = path;
            r.Args = tArgs.Text.Trim();
            r.ProcessName = tProc.Text.Trim();
            r.Delay = (int)nDelay.Value;
            r.Admin = cAdmin.Checked;
            r.SkipIfRunning = cSkip.Checked;
            r.UpdateKind = kind;
            r.Repo = tRepo.Text.Trim();
            r.Asset = tAsset.Text.Trim();
            r.Special = sp;
            r.HappAutoConnect = cHappAuto.Checked;
            r.VpnServer = (int)nKoalaServer.Value;
            r.HappPresets = presets.Select(p => p.Copy()).ToList();
            r.HappServer = cbServer.SelectedIndex > 0 ? presets[cbServer.SelectedIndex - 1].Name : "";
            r.HostsFixDir = tHostsDir.Text.Trim();
            r.SpotifyCheckHosts = cSpotifyCheck.Checked;
            r.Watch = cWatch.Checked;
            r.WaitReady = cWait.Checked;
            if (cOwnAuto != null && cOwnAuto.Checked != AutoStartCtl.IsEnabled(autoEntry))
            {
                try { AutoStartCtl.SetEnabled(autoEntry, cOwnAuto.Checked); }
                catch (Exception ex) { Dlg.Info(this, "Не получилось", "Не удалось изменить автозапуск: " + ex.Message); }
            }
            r.Message = null;
            r.MessageIsError = false;
            if (updChanged) { r.LatestTag = null; r.LatestUrl = null; r.LocalVersion = null; r.CheckError = null; }
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------------------------------------------------------- выбор: «Пуск» или запущенные программы

    public class PickerForm : ThemedForm
    {
        public PickRow Picked;
        readonly TextField search;
        readonly RowList list;
        readonly Toggle sysToggle;
        readonly Func<bool, List<PickRow>> load;
        readonly bool loadInBackground;
        readonly Label lblInfo;
        List<PickRow> all = new List<PickRow>();
        int generation;

        public static PickerForm StartMenu()
        {
            return new PickerForm("Из меню «Пуск»", "Все программы из «Пуска», включая приложения из Microsoft Store.", false, false,
                sys => Core.StartApps().Select(a =>
                {
                    bool file = false;
                    try { file = File.Exists(a.Value); } catch { }
                    return new PickRow { Title = a.Key, Value = a.Value, Sub = file ? a.Value : "приложение Windows / Microsoft Store" };
                }).ToList());
        }

        public static PickerForm Running()
        {
            return new PickerForm("Из запущенных программ", "Программы, которые работают прямо сейчас. Сверху — те, у которых есть окно.", true, true,
                sys => Core.RunningApps().Where(a => sys || !a.System)
                    .Select(a => new PickRow { Title = a.Title, Sub = a.Path, Value = a.Path, Tag = a }).ToList());
        }

        PickerForm(string title, string sub, bool withSystemToggle, bool background, Func<bool, List<PickRow>> load)
        {
            this.load = load;
            loadInBackground = background;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(S(640), S(620));
            MinimumSize = SizeFromClientSize(new Size(S(480), S(420)));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(22), S(16), S(22), S(18)) };
            Controls.Add(root);
            Label head = Lbl(title, "title");
            head.Margin = new Padding(0, 0, 0, S(2));
            Label subL = Lbl(sub, "dim");
            subL.Margin = new Padding(0, 0, 0, S(12));
            search = new TextField("", S(590)) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, S(10)) };
            SetCue(search.Box, "Поиск по названию или пути");
            search.Box.TextChanged += delegate { Filter(); };
            search.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down) { list.Step(1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { list.Step(-1); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { Pick(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            list = new RowList { Dock = DockStyle.Fill, Margin = new Padding(0), Empty = "Загружаю список…" };
            list.Activate += delegate { Pick(); };
            lblInfo = Lbl("", "dim");
            lblInfo.Margin = new Padding(0, S(8), 0, 0);

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(head);
            root.Controls.Add(subL);
            root.Controls.Add(search);
            if (withSystemToggle)
            {
                sysToggle = new Toggle("Показать системные процессы Windows", null, false);
                sysToggle.Fit();
                sysToggle.Margin = new Padding(0, 0, 0, S(8));
                sysToggle.CheckedChanged += async delegate { await Reload(); };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(sysToggle);
            }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(list);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(lblInfo);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(14), 0, 0) };
            var cancel = new FlatBtn("Отмена", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            cancel.Fit();
            var ok = new FlatBtn("Выбрать", BtnKind.Primary, null) { Margin = new Padding(0, 3, S(10), 3) };
            ok.Fit();
            ok.Click += delegate { Pick(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(buttons);
            CancelButton = cancel;

            if (!loadInBackground) { Cursor = Cursors.WaitCursor; all = load(false); Cursor = Cursors.Default; Filter(); StartIcons(); }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            search.Box.Focus();
            if (loadInBackground) await Reload();
        }

        async Task Reload()
        {
            bool sys = sysToggle != null && sysToggle.Checked;
            list.Empty = "Загружаю список…";
            list.SetRows(new List<PickRow>());
            all = await Task.Run(() => load(sys));
            list.Empty = "Ничего не найдено";
            Filter();
            StartIcons();
        }

        void Filter()
        {
            string q = search.Text.Trim();
            List<PickRow> rows = q.Length == 0 ? all.ToList() : all.Where(r =>
                (r.Title ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (r.Sub ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            list.SetRows(rows);
            lblInfo.Text = all.Count == 0 ? "" : string.Format("Показано {0} из {1}", rows.Count, all.Count);
        }

        // Значки грузятся в фоне (их бывает несколько сотен).
        void StartIcons()
        {
            int gen = ++generation;
            List<PickRow> snapshot = all.ToList();
            int size = S(28) * 2;
            var th = new Thread(() =>
            {
                int n = 0;
                foreach (PickRow r in snapshot)
                {
                    if (gen != generation || IsDisposed) return;
                    if (r.Image != null) continue;
                    string p = r.Tag is Core.RunningApp ? r.Value : Core.StartAppToPath(r.Value);
                    r.Image = IconLoader.ForPath(p, size);
                    if (++n % 12 == 0) try { BeginInvoke((Action)(() => list.Invalidate())); } catch { return; }
                }
                try { BeginInvoke((Action)(() => list.Invalidate())); } catch { }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { generation++; base.OnFormClosed(e); }

        void Pick()
        {
            if (list.Current == null) return;
            Picked = list.Current;
            DialogResult = DialogResult.OK;
            Close();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        static void SetCue(TextBox tb, string text)
        {
            if (tb.IsHandleCreated) SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
            else tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); };
        }
    }

    // ---------------------------------------------------------------- настройки

    public class SettingsForm : ThemedForm
    {
        readonly Config cfg;
        readonly Action onTheme, onChange;
        public string Request;      // что сделать после закрытия окна: "uninstall", "import", "reset", "resetall"
        public string RequestFile;  // для импорта — выбранный файл
        readonly FlatBtn[] themeBtns;
        static readonly string[] Modes = { "dark", "light", "system" };

        public SettingsForm(Config cfg, Action onTheme, Action onChange)
        {
            this.cfg = cfg;
            this.onTheme = onTheme;
            this.onChange = onChange;
            Text = "Настройки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // две колонки — иначе окно выше экрана
            var outer = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(S(26), S(18), S(26), S(22)) };
            Controls.Add(outer);
            var left = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 0, S(32), 0) };
            var right = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
            outer.Controls.Add(left, 0, 0);
            outer.Controls.Add(right, 1, 0);
            var t = left;

            t.Controls.Add(Sec("ТЕМА", true));
            var themes = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, S(4)), WrapContents = false };
            string[] names = { "Тёмная", "Светлая", "Как в Windows" };
            themeBtns = new FlatBtn[3];
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = new FlatBtn(names[i], BtnKind.Secondary, null) { Margin = new Padding(0, 0, S(8), 0) };
                b.Fit();
                b.Click += delegate { SetTheme(Modes[k]); };
                themeBtns[i] = b;
                themes.Controls.Add(b);
            }
            t.Controls.Add(themes);
            MarkTheme();

            t.Controls.Add(Sec("ЗАПУСК", false));
            AddToggle(t, "Жить в трее, возле часов", "Закрытие окна прячет Старт-пак в трей: перезапуск закрывшихся программ и Ctrl+Alt+S продолжают работать. Выйти — через меню «⋯» или трей.", cfg.Tray,
                on => { cfg.Tray = on; Changed(); return true; });
            AddToggle(t, "Горячая клавиша Ctrl+Alt+S — запустить всё", "Работает из любой программы, пока Старт-пак открыт или в трее.", cfg.Hotkey,
                on => { cfg.Hotkey = on; Changed(); return true; });
            AddToggle(t, "Прятать окно после «Запустить всё»", "Если всё запустилось без ошибок и обновлений нет: в трей, а без трея — закрыть.", cfg.CloseAfterLaunch,
                on => { cfg.CloseAfterLaunch = on; return true; });
            AddToggle(t, "Проверять связь после запуска", "YouTube, Discord, Spotify и прокси Telegram — зелёная точка значит, что отвечают.", cfg.NetCheck,
                on => { cfg.NetCheck = on; return true; });
            AddToggle(t, "Проверять обновления при открытии", "zapret и TG WS Proxy — по релизам на GitHub.", cfg.CheckUpdatesOnStart,
                on => { cfg.CheckUpdatesOnStart = on; return true; });
            AddToggle(t, "Запускать всё при входе в Windows", "Ярлык в «Автозагрузке»: после входа всё стартует само, в фоне — окно не появится.", Core.AutostartEnabled(),
                on => { Core.SetAutostart(on); return Core.AutostartEnabled() == on; });
            AddToggle(t, "Ярлык «Запустить всё» на рабочем столе", "Двойной щелчок по нему — и всё запускается без лишних кликов.", Core.RunAllShortcutExists(),
                on => { Core.SetRunAllShortcut(on); return Core.RunAllShortcutExists() == on; });

            t = right;
            t.Controls.Add(Sec("ОБНОВЛЕНИЯ И АВТОЗАПУСК", true));
            AddToggle(t, "Напоминать об обновлениях", "Раз в 6 часов Старт-пак проверяет новые версии и, если он в трее, присылает уведомление.", cfg.UpdateNotify,
                on => { cfg.UpdateNotify = on; return true; });
            Label dl = Lbl("Задержка «Запустить всё» при входе в Windows — чтобы сеть и VPN успели подняться:", "dim");
            dl.MaximumSize = new Size(S(520), 0);
            dl.Margin = new Padding(0, S(8), 0, S(6));
            t.Controls.Add(dl);
            var delays = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, S(4)), WrapContents = false };
            int[] secs = { 0, 15, 30, 60 };
            var delayBtns = new List<FlatBtn>();
            foreach (int sec in secs)
            {
                int ss = sec;
                var b = new FlatBtn(sec == 0 ? "Без задержки" : sec + " с", cfg.AutorunDelay == sec ? BtnKind.Primary : BtnKind.Secondary, null) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
                b.Fit();
                b.Click += delegate
                {
                    cfg.AutorunDelay = ss;
                    foreach (FlatBtn x in delayBtns) { x.Kind = x == b ? BtnKind.Primary : BtnKind.Secondary; x.Invalidate(); }
                    try { Core.Save(cfg); } catch { }
                };
                delayBtns.Add(b);
                delays.Controls.Add(b);
            }
            t.Controls.Add(delays);

            t.Controls.Add(Sec("ОВЕРЛЕЙ", false));
            AddToggle(t, "Показывать оверлей поверх окон", "Маленькая панель: что запущено, VPN и связь. Ctrl+Alt+O — показать/скрыть.", cfg.Overlay,
                on => { cfg.Overlay = on; Changed(); return true; });
            var ovBtn = new FlatBtn("Настроить оверлей", BtnKind.Secondary, Glyph.Settings) { Compact = true, Margin = new Padding(0, S(2), 0, 0) };
            ovBtn.Fit();
            ovBtn.Click += delegate { using (var f = new OverlaySettingsForm(cfg, onChange)) f.ShowDialog(this); };
            t.Controls.Add(ovBtn);

            t.Controls.Add(Sec("НАСТРОЙКИ И СПИСОК", false));
            Label sl = Lbl("Сохраните настройки и список программ в файл — чтобы перенести на другой ПК или вернуть потом.", "dim");
            sl.MaximumSize = new Size(S(520), 0);
            sl.Margin = new Padding(0, 0, 0, S(8));
            t.Controls.Add(sl);
            var cfgRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            var exp = new FlatBtn("Экспорт", BtnKind.Secondary, Glyph.Export) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
            exp.Fit();
            exp.Click += delegate { ExportCfg(); };
            var imp = new FlatBtn("Импорт", BtnKind.Secondary, Glyph.Import) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
            imp.Fit();
            imp.Click += delegate { ImportCfg(); };
            var rst = new FlatBtn("Сброс", BtnKind.Secondary, Glyph.Reset) { Compact = true, Margin = new Padding(0) };
            rst.Fit();
            rst.Click += delegate { ResetMenu(rst); };
            cfgRow.Controls.Add(exp);
            cfgRow.Controls.Add(imp);
            cfgRow.Controls.Add(rst);
            t.Controls.Add(cfgRow);

            t.Controls.Add(Sec("О ПРОГРАММЕ И ОБНОВЛЕНИЯ", false));
            Label about = Lbl("Старт-пак " + Core.Version, "");
            about.Font = Fonts.Semi(10.5f);
            about.Margin = new Padding(0, 0, 0, S(2));
            t.Controls.Add(about);
            new ToolTip().SetToolTip(about, "Список программ хранится в " + Core.ConfigPath);
            selfState = Lbl(SelfStateText(), "dim");
            selfState.MaximumSize = new Size(S(520), 0);
            selfState.Margin = new Padding(0, 0, 0, S(8));
            t.Controls.Add(selfState);
            var updRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, S(8)) };
            selfBtn = new FlatBtn("Проверить обновления", BtnKind.Secondary, Glyph.Refresh) { Compact = true, Margin = new Padding(0, 0, S(8), 0) };
            MarkSelfBtn();
            selfBtn.Click += async delegate { await SelfBtnClick(); };
            var gh = new FlatBtn("Проект на GitHub", BtnKind.Secondary, Glyph.Web) { Compact = true, Margin = new Padding(0) };
            gh.Fit();
            gh.Click += delegate { try { Core.ShellOpen(SelfUpdate.Page); } catch { } };
            new ToolTip().SetToolTip(gh, SelfUpdate.Page);
            updRow.Controls.Add(selfBtn);
            updRow.Controls.Add(gh);
            t.Controls.Add(updRow);
            var openDir = new FlatBtn("Открыть папку программы", BtnKind.Secondary, Glyph.Folder) { Compact = true, Margin = new Padding(0) };
            openDir.Fit();
            openDir.Click += delegate { try { Core.ShellOpen(Core.AppDir); } catch { } };
            var setupRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            setupRow.Controls.Add(openDir);
            if (Setup.IsInstalled)
            {
                var setupBtn = new FlatBtn("Удалить Старт-пак", BtnKind.Secondary, Glyph.Delete) { Compact = true, Margin = new Padding(S(8), 0, 0, 0) };
                setupBtn.Click += delegate
                {
                    if (!Dlg.Ask(this, "Удалить Старт-пак?", "Удалятся программа, её ярлыки и список программ. Сами программы — zapret, Discord, VPN и другие — останутся.", "Удалить", "Отмена")) return;
                    Request = "uninstall";
                    Close();
                };
                setupBtn.Fit();
                setupRow.Controls.Add(setupBtn);
            }
            t.Controls.Add(setupRow);
            if (Core.HasDonate(cfg))
            {
                var thanks = new LinkLabel
                {
                    Text = "Нравится Старт-пак? Поблагодарить автора", AutoSize = true, Font = Fonts.Ui(9f), Margin = new Padding(0, S(12), 0, 0),
                    LinkColor = Theme.Current.TextDim, ActiveLinkColor = Theme.Current.Accent, VisitedLinkColor = Theme.Current.TextDim,
                    LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent
                };
                thanks.LinkClicked += delegate { using (var f = new DonateForm(cfg)) f.ShowDialog(this); };
                t.Controls.Add(thanks);
            }

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(20), 0, 0) };
            var done = new FlatBtn("Готово", BtnKind.Primary, null) { DialogResult = DialogResult.OK };
            done.Fit();
            buttons.Controls.Add(done);
            outer.Controls.Add(buttons, 0, 1);
            outer.SetColumnSpan(buttons, 2);
            AcceptButton = done;
            CancelButton = done;
        }

        void Changed() { if (onChange != null) onChange(); }

        Label selfState;
        FlatBtn selfBtn;

        static string SelfStateText()
        {
            SelfUpdate.Info i = SelfUpdate.Last;
            if (i != null && i.Newer) return "Вышла версия " + i.Version + ". Обновление скачается с GitHub, проверится и поставится само — список и настройки сохранятся.";
            if (i != null) return "Это последняя версия.";
            if (SelfUpdate.LastError != null) return "Проверить не удалось: " + SelfUpdate.LastError + ".";
            return "Обновления берутся с GitHub-страницы проекта.";
        }

        void MarkSelfBtn()
        {
            bool newer = SelfUpdate.Last != null && SelfUpdate.Last.Newer;
            selfBtn.Text = newer ? "Обновить до " + SelfUpdate.Last.Version : "Проверить обновления";
            selfBtn.Kind = newer ? BtnKind.Primary : BtnKind.Secondary;
            selfBtn.Icon = newer ? Glyph.Download : Glyph.Refresh;
            selfBtn.Fit();
            selfBtn.Invalidate();
        }

        async System.Threading.Tasks.Task SelfBtnClick()
        {
            if (SelfUpdate.Last != null && SelfUpdate.Last.Newer) { Request = "selfupdate"; Close(); return; }
            selfBtn.Enabled = false;
            selfState.Text = "Проверяю…";
            try { await SelfUpdate.Check(); } catch { }
            if (IsDisposed) return;
            selfState.Text = SelfStateText();
            selfBtn.Enabled = true;
            MarkSelfBtn();
        }

        void ExportCfg()
        {
            using (var d = new SaveFileDialog
            {
                Title = "Сохранить настройки Старт-пака",
                Filter = "Настройки Старт-пака (*.json)|*.json",
                FileName = "Старт-пак — настройки " + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Core.ExportTo(cfg, d.FileName);
                    Dlg.Info(this, "Сохранено", "Настройки и список программ — в файле:\n" + d.FileName + "\n\nНа другом ПК: «Настройки» → «Импорт…».");
                }
                catch (Exception ex) { Dlg.Info(this, "Не получилось сохранить", ex.Message); }
            }
        }

        void ImportCfg()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Загрузить настройки Старт-пака",
                Filter = "Настройки Старт-пака (*.json)|*.json|Все файлы|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { Core.FromFile(d.FileName); }
                catch (Exception ex) { Dlg.Info(this, "Не получилось загрузить", ex.Message); return; }
                if (!Dlg.Ask(this, "Заменить настройки и список?", "Текущие настройки и список программ заменятся загруженными. Пути к программам Старт-пак подстроит под этот ПК.\n\nПрежние сохранятся в StartPack.json.bak.", "Заменить", "Отмена")) return;
                Request = "import";
                RequestFile = d.FileName;
                Close();
            }
        }

        void ResetMenu(Control at)
        {
            ContextMenuStrip m = Menus.New();
            Menus.Add(m.Items, "Сбросить настройки (список останется)", Glyph.Reset, delegate
            {
                if (!Dlg.Ask(this, "Сбросить настройки?", "Тема, трей, горячая клавиша, оверлей, напоминания и запуск с Windows вернутся к начальным. Список программ и профили останутся.\n\nПрежние настройки сохранятся в StartPack.json.bak.", "Сбросить", "Отмена")) return;
                Request = "reset";
                Close();
            });
            ToolStripMenuItem all = Menus.Add(m.Items, "Сбросить всё — и настройки, и список", Glyph.Delete, delegate
            {
                if (!Dlg.Ask(this, "Сбросить всё?", "Настройки станут начальными, а список программ — стандартным набором (все программы, VPN выключены). Сами программы не удаляются.\n\nПрежние настройки и список сохранятся в StartPack.json.bak.", "Сбросить всё", "Отмена")) return;
                Request = "resetall";
                Close();
            });
            all.ForeColor = Theme.Current.Red;
            m.Show(at, new Point(0, at.Height));
        }

        Label Sec(string text, bool first)
        {
            Label l = Lbl(text, "section");
            l.Margin = new Padding(0, first ? 0 : S(20), 0, S(8));
            return l;
        }

        void AddToggle(TableLayoutPanel t, string text, string hint, bool on, Func<bool, bool> apply)
        {
            var tg = new Toggle(text, hint, on) { Margin = new Padding(0, S(4), 0, S(6)) };
            tg.Fit();
            bool reverting = false;
            tg.CheckedChanged += delegate
            {
                if (reverting) return;
                bool ok;
                try { ok = apply(tg.Checked); }
                catch (Exception ex) { ok = false; Dlg.Info(this, "Не получилось", ex.Message); }
                if (!ok) { reverting = true; tg.Checked = !tg.Checked; reverting = false; }
                try { Core.Save(cfg); } catch { }
            };
            t.Controls.Add(tg);
        }

        void SetTheme(string mode)
        {
            cfg.Theme = mode;
            Theme.Apply(mode);
            Restyle(this);
            MarkTheme();
            if (onTheme != null) onTheme();
        }

        void MarkTheme()
        {
            for (int i = 0; i < 3; i++)
            {
                themeBtns[i].Kind = Modes[i] == Theme.Mode ? BtnKind.Primary : BtnKind.Secondary;
                themeBtns[i].Invalidate();
            }
        }
    }
}
