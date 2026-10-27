// Главное окно: Happ, Spotify и hosts, zapret (стратегии, остановка, тест), обновления, проверка связи.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm
    {
        // ---------------------------------------------------------------- кнопки на карточках

        async Task OnChip(Item it, string id)
        {
            switch (id)
            {
                case "connect":
                    running = Core.RunningNames();
                    if (!Core.IsRunning(it, running))
                    {
                        bool auto = it.HappAutoConnect;
                        it.HappAutoConnect = true;
                        await RunVpn(it, false);
                        it.HappAutoConnect = auto;
                    }
                    else await VpnConnect(it);
                    break;
                case "disconnect":
                    await VpnDisconnect(it);
                    break;
                case "koalaserver":
                case "vpnserver":
                    ShowVpnServers(it);
                    break;
                case "zapretsvc":
                    await ZapretTakeOver(it);
                    break;
                case "checkhosts":
                    await CheckHosts(it, true);
                    break;
                case "fixhosts":
                    await FixHosts(it, Core.RunningNames().Contains("Spotify"));
                    break;
                case "install":
                    string kind = Installers.KindOf(it);
                    if (kind != null) await InstallApp(kind, it, false);
                    break;
                case "cancel":
                    CancelDownload(it);
                    break;
                case "close":
                    await StopItem(it);
                    break;
                case "site":
                    Installers.OpenPage(this, Installers.Get(Installers.KindOf(it)));
                    break;
                case "strategy":
                    ContextMenuStrip m = Menus.New();
                    m.ShowImageMargin = false;
                    m.ShowCheckMargin = true;
                    FillStrategies(m.Items, it);
                    m.Items.Add(new ToolStripSeparator());
                    Menus.Add(m.Items, "Подобрать тестом", null, async delegate { await ZapretTest(it); });
                    lastMenu = m;
                    m.Show(cards, cards.PointToClient(Cursor.Position));
                    break;
            }
            RefreshState();
        }

        // ---------------------------------------------------------------- Happ

        async Task SwitchHappServer(Item it, HappPreset p)
        {
            if (!Ask("Переключить сервер Happ?", "Happ закроется и откроется заново, подключившись к «" + p.Name + "». Интернет через VPN пропадёт на несколько секунд.", "Переключить", "Отмена")) return;
            it.HappServer = p.Name;
            SaveCfg();
            it.Busy = true;
            Journal.Write("happ", "переключение на «" + p.Name + "»");
            try
            {
                SetMsg(it, "отключаю…", false);
                await HappCtl.Disconnect();
                SetMsg(it, "закрываю Happ…", false);
                if (!await HappCtl.Close()) throw new Exception("Happ не закрылся — закройте его вручную и запустите снова");
                HappCtl.Apply(p);
                it.Busy = false;
                bool auto = it.HappAutoConnect;
                it.HappAutoConnect = true;
                running = Core.RunningNames();
                int r = await RunVpn(it, false);
                it.HappAutoConnect = auto;
                Status(r == 0 ? "Happ подключён к «" + p.Name + "»." : "Happ открыт, но не подключился к «" + p.Name + "».");
            }
            catch (Exception ex)
            {
                it.Busy = false;
                SetMsg(it, ex.Message, true);
                Journal.Write("happ", "переключение не удалось: " + ex.Message);
            }
            RefreshState();
        }

        void CaptureHappServer(Item it)
        {
            HappPreset p = HappCtl.Capture();
            if (p == null)
            {
                Dlg.Info(this, "Сервер не найден", "В настройках Happ нет выбранного сервера. Подключитесь в Happ к нужному серверу и повторите.");
                return;
            }
            if (it.HappPresets.Any(x => x.Guid == p.Guid))
            {
                Dlg.Info(this, "Уже сохранён", "Сервер «" + p.Name + "» уже есть в списке сохранённых.");
                return;
            }
            string name = p.Name;
            for (int n = 2; it.HappPresets.Any(x => x.Name == p.Name); n++) p.Name = name + " (" + n + ")";
            it.HappPresets.Add(p);
            if (Ask("Сервер сохранён", "«" + p.Name + "» сохранён.\n\nПодключаться к нему, когда Happ запускается из Старт-пака?", "Да, к нему", "Нет")) it.HappServer = p.Name;
            SaveCfg();
            Journal.Write("happ", "запомнен сервер «" + p.Name + "»");
            cards.Invalidate();
        }

        // ---------------------------------------------------------------- Spotify и hosts

        async Task CheckAllHosts()
        {
            foreach (Item it in cfg.Items.Where(i => i.Special == "spotify" && HostsFix.Valid(i.HostsFixDir)).ToList())
                await CheckHosts(it, false);
        }

        async Task<HostsFix.ProbeResult> CheckHosts(Item it, bool report)
        {
            if (!HostsFix.Valid(it.HostsFixDir)) return null;
            it.HostsState = "checking";
            cards.Invalidate();
            HostsFix.ProbeResult pr = await HostsFix.Probe(it.HostsFixDir);
            it.HostsIp = pr.CurrentIp ?? "";
            it.HostsState = !pr.Ran ? "error" : pr.CurrentOk ? "ok" : "dead";
            string line = "ip " + it.HostsIp + ", текущий " + (pr.CurrentOk ? "отвечает" : "не отвечает") + ", рабочие есть: " + (pr.AnyOk ? "да" : "нет") + (pr.Error != null ? ", ошибка " + pr.Error : "");
            Journal.Write("spotify", "проверка hosts: " + line);
            Log("hosts Spotify: " + line);
            if (report)
            {
                if (!pr.Ran) Status("Не удалось проверить hosts: " + (pr.Error ?? "скрипт не ответил"));
                else if (pr.CurrentOk) Status("hosts Spotify в порядке: " + it.HostsIp + " отвечает.");
                else if (pr.AnyOk) Status("Адрес " + (it.HostsIp.Length > 0 ? it.HostsIp : "в hosts") + " не отвечает, но рабочие есть — нажмите «Починить hosts».");
                else Status("Сейчас не работает ни один адрес для Spotify. Подробности — в logs\\update.log в папке SpotifyHostsFix.");
            }
            cards.Invalidate();
            return pr;
        }

        async Task PrepareSpotify(Item it)
        {
            if (!it.SpotifyCheckHosts || !HostsFix.Valid(it.HostsFixDir)) return;
            it.Busy = true;
            SetMsg(it, "проверяю hosts…", false);
            HostsFix.ProbeResult pr = await CheckHosts(it, false);
            if (pr != null && pr.Ran && !pr.CurrentOk && pr.AnyOk)
            {
                SetMsg(it, "чиню hosts…", false);
                await FixHosts(it, false);
            }
            it.Busy = false;
            SetMsg(it, null, false);
        }

        async Task FixHosts(Item it, bool restartSpotify)
        {
            it.HostsState = "fixing";
            bool wasBusy = it.Busy;
            it.Busy = true;
            SetMsg(it, "чиню hosts…", false);
            try
            {
                if (restartSpotify) MarkExpected(it, true);
                HostsFix.FixResult fr = await HostsFix.RunElevated(it.HostsFixDir, restartSpotify ? "-RestartSpotify" : "");
                it.HostsIp = HostsFix.CurrentIps().FirstOrDefault() ?? "";
                it.HostsState = fr.ExitCode == 0 ? "ok" : "dead";
                string msg = fr.Message ?? (fr.ExitCode == 0 ? "готово." : "не получилось (код " + fr.ExitCode + ").");
                Status("Spotify hosts: " + msg);
                Journal.Write("spotify", "починка hosts: " + msg);
            }
            catch (Win32Exception)
            {
                it.HostsState = "dead";
                Status("Починка hosts отменена — для неё нужны права администратора.");
                Journal.Write("spotify", "починка hosts отменена (нет прав)");
            }
            catch (Exception ex)
            {
                it.HostsState = "error";
                Status("Не получилось починить hosts: " + ex.Message);
                Journal.Write("spotify", "починка hosts: ошибка " + ex.Message);
            }
            it.Busy = wasBusy;
            SetMsg(it, wasBusy ? it.Message : null, false);
        }

        async Task ToggleHostsTask(Item it, bool on)
        {
            it.Busy = true;
            SetMsg(it, on ? "включаю автообновление…" : "выключаю автообновление…", false);
            try
            {
                HostsFix.FixResult fr = await HostsFix.RunElevated(it.HostsFixDir, on ? "-Install" : "-Uninstall");
                string msg = fr.Message ?? (on ? "Автообновление hosts включено." : "Автообновление hosts выключено.");
                Status(msg);
                Journal.Write("spotify", msg);
            }
            catch (Win32Exception) { Status("Отменено — для этого нужны права администратора."); }
            catch (Exception ex) { Status("Не получилось: " + ex.Message); }
            it.Busy = false;
            SetMsg(it, null, false);
        }

        // ---------------------------------------------------------------- zapret: стоп, перезапуск, стратегии, тест

        // Кнопка ■ на карточке и в оверлее: закрыть программу (VPN — сначала отключить).
        async Task StopItem(Item it)
        {
            if (it.Busy) return;
            if (it.UpdateKind == "zapret") { await ZapretStop(it); return; }
            string[] names = Core.ProcNames(it);
            if (names.Length == 0) return;
            MarkExpected(it, true);
            string err = null;
            try
            {
                if (VpnCtl.IsVpn(it) && await ConnectedNow(it)) await VpnDisconnect(it);
                it.Busy = true;
                SetMsg(it, "закрываю…", false);
                if (it.Special == "happ")
                {
                    if (!await HappCtl.Close()) err = "Happ не закрылся";
                }
                else
                {
                    bool denied = false;
                    await Task.Run(() =>
                    {
                        foreach (string n in names)
                            foreach (Process p in Process.GetProcessesByName(n))
                            {
                                try { p.Kill(); p.WaitForExit(3000); }
                                catch (Win32Exception) { denied = true; }
                                catch { }
                                finally { p.Dispose(); }
                            }
                    });
                    if (denied)
                    {
                        // запущена от имени администратора (например, Koala Clash) — Windows спросит разрешение
                        var psi = new ProcessStartInfo("cmd.exe", "/c taskkill /F" + string.Concat(names.Select(n => " /IM \"" + n + ".exe\"")))
                        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                        using (Process p = Process.Start(psi)) await Task.Run(() => p.WaitForExit(10000));
                    }
                }
                bool gone = await HappCtl.WaitFor(() => !Core.IsRunning(it, Core.RunningNames()), 8000);
                if (!gone && err == null) err = "программа не закрылась";
            }
            catch (Win32Exception) { err = "отменено — нужны права администратора"; }
            catch (Exception ex) { err = ex.Message; }
            it.Busy = false;
            SetMsg(it, err == null ? "остановлена" : "не остановилась: " + err, err != null);
            Journal.Write("стоп", it.Name + (err == null ? ": остановлена" : ": " + err));
            RefreshState();
        }

        async Task ZapretStop(Item it)
        {
            try
            {
                MarkExpected(it, true);
                ZapretCtl.Stop();
                it.Busy = true;
                SetMsg(it, "останавливаю…", false);
                bool stopped = await HappCtl.WaitFor(() => !Core.RunningNames().Contains("winws"), 10000);
                it.Busy = false;
                SetMsg(it, stopped ? "остановлен" : "не остановился", !stopped);
                Journal.Write("zapret", stopped ? "остановлен" : "не остановился");
            }
            catch (Win32Exception) { it.Busy = false; Status("Отменено — чтобы остановить zapret, нужны права администратора."); }
            RefreshState();
        }

        async Task ZapretRestart(Item it)
        {
            try
            {
                MarkExpected(it, true);
                Core.RestartZapret(Core.Expand(it.Path));
                it.Busy = true;
                SetMsg(it, "перезапускаю…", false);
                await Task.Delay(2500);
                bool up = await HappCtl.WaitFor(() => Core.RunningNames().Contains("winws"), 20000);
                it.Busy = false;
                SetMsg(it, up ? null : "не запустился после перезапуска", !up);
                Journal.Write("zapret", "перезапуск со стратегией " + ZapretCtl.Pretty(ZapretCtl.Current(it)) + (up ? ": работает" : ": не поднялся"));
            }
            catch (Win32Exception) { it.Busy = false; Status("Отменено — чтобы перезапустить zapret, нужны права администратора."); }
            RefreshState();
        }

        // Драйвер WinDivert / служба zapret остались от другой папки: снять их и запустить zapret из выбранной.
        async Task ZapretTakeOver(Item it)
        {
            var foreign = ZapretCtl.Foreign(ZapretCtl.Dir(it));
            if (foreign.Count == 0) { cards.Invalidate(); return; }
            string old = string.Join(", ", foreign.Select(s => "«" + Path.GetFileName(s.Root) + "»").Distinct());
            if (!Ask("Перейти на эту папку?", "Из папки " + old + " ещё работает " + ZapretCtl.Describe(foreign) +
                ". Из-за этого старую папку нельзя удалить, а zapret может работать со старым драйвером.\n\n" +
                "Старт-пак снимет его и запустит zapret из «" + Path.GetFileName(ZapretCtl.Dir(it)) + "». Windows спросит права администратора.",
                "Перейти", "Отмена")) return;
            await ZapretRestart(it);
            ZapretCtl.ForgetServices();
            Detect.ForgetZapret();
        }

        async Task ZapretSwitch(Item it, string bat)
        {
            string dir = ZapretCtl.Dir(it);
            if (dir == null) return;
            it.Path = Path.Combine(dir, bat);
            SaveCfg();
            Journal.Write("zapret", "стратегия → " + ZapretCtl.Pretty(bat));
            if (Core.RunningNames().Contains("winws"))
            {
                if (Ask("Перезапустить zapret?", "Стратегия «" + ZapretCtl.Pretty(bat) + "» выбрана. Перезапустить zapret, чтобы она заработала? Windows попросит права администратора.", "Перезапустить", "Позже"))
                    await ZapretRestart(it);
                else Status("Стратегия «" + ZapretCtl.Pretty(bat) + "» включится при следующем запуске zapret.");
            }
            else Status("Стратегия «" + ZapretCtl.Pretty(bat) + "» выбрана — она будет при следующем запуске.");
            cards.Invalidate();
        }

        async Task ZapretTest(Item it)
        {
            string ps1 = ZapretCtl.TestScript(it);
            if (ps1 == null || !File.Exists(ps1)) { Dlg.Info(this, "Теста нет", "В этой версии zapret нет utils\\test zapret.ps1."); return; }
            if (!Ask("Подобрать стратегию?", "Откроется тест zapret: он по очереди пробует стратегии и проверяет Discord, YouTube и другие сайты. Это займёт несколько минут, zapret на это время перезапускается.\n\nВ окне теста выберите «1» (обычный тест) и «0» (все стратегии). Когда тест закончится, Старт-пак предложит лучшую.", "Начать тест", "Отмена")) return;
            // Через VPN тест врёт: сайты открываются и без zapret. Предложить отключить VPN на время теста.
            List<Item> paused = await PauseVpnsForTest();
            if (paused == null) return;
            DateTime since = DateTime.Now;
            Process p = null;
            bool denied = false;
            try { MarkExpected(it, true); p = ZapretCtl.StartTest(it); }
            catch (Win32Exception) { denied = true; }
            if (denied) { Status("Отменено — для теста нужны права администратора."); await ResumeVpns(paused); return; }
            Journal.Write("zapret", "запущен тест стратегий" + (paused.Count > 0 ? " (VPN на время теста отключены)" : ""));
            it.Busy = true;
            SetMsg(it, "идёт тест стратегий…", false);
            if (p != null) using (p) await Task.Run(() => p.WaitForExit());
            it.Busy = false;
            SetMsg(it, null, false);
            await ResumeVpns(paused);
            string summary;
            string best = ZapretCtl.BestFromResults(it, since, out summary);
            if (best == null) { Status("Тест закрыт без результата."); Journal.Write("zapret", "тест без результата"); return; }
            Journal.Write("zapret", "тест: лучшая — " + summary);
            if (best.Equals(ZapretCtl.Current(it), StringComparison.OrdinalIgnoreCase))
            {
                Dlg.Info(this, "Стратегия уже лучшая", "По тесту лучшая — «" + ZapretCtl.Pretty(best) + "», она у вас и так выбрана.");
                return;
            }
            if (Ask("Лучшая стратегия — «" + ZapretCtl.Pretty(best) + "»", summary + ".\n\nПереключиться на неё?", "Переключить", "Оставить как есть"))
                await ZapretSwitch(it, best);
        }

        // Подключённые VPN. null — человек отменил тест.
        async Task<List<Item>> PauseVpnsForTest()
        {
            var on = new List<Item>();
            foreach (Item v in cfg.Items.Where(VpnCtl.IsVpn))
                if (Core.IsRunning(v, Core.RunningNames()) && await ConnectedNow(v)) on.Add(v);
            if (on.Count == 0) return on;
            string names = string.Join(", ", on.Select(v => v.Name));
            using (var f = new MsgForm("Сейчас подключён VPN (" + names + ")",
                "Через VPN тест покажет неправду: Discord и YouTube будут открываться и без zapret.\n\n" +
                "Старт-пак отключит VPN на время теста и подключит обратно, когда тест закончится.", "Отключить на время теста", "Тестировать с VPN"))
            {
                DialogResult r = f.ShowDialog(this);
                if (r == DialogResult.Cancel) return null;
                if (r != DialogResult.Yes) return new List<Item>();
            }
            var paused = new List<Item>();
            foreach (Item v in on)
            {
                await VpnDisconnect(v);
                if (!await ConnectedNow(v)) paused.Add(v);
            }
            if (paused.Count > 0) Journal.Write("zapret", "на время теста отключены: " + string.Join(", ", paused.Select(v => v.Name)));
            return paused;
        }

        async Task ResumeVpns(List<Item> paused)
        {
            foreach (Item v in paused)
            {
                if (!Core.IsRunning(v, Core.RunningNames())) continue;   // закрыли сами — не открываем
                await VpnConnect(v);
            }
            if (paused.Count > 0) Status("Тест закончен, VPN подключён обратно: " + string.Join(", ", paused.Select(v => v.Name)) + ".");
        }

        // ---------------------------------------------------------------- проверка связи

        List<NetTarget> netTargets = new List<NetTarget>();
        bool netChecking;

        void SyncNetTargets()
        {
            List<NetTarget> fresh = NetCheck.TargetsFor(cfg);
            foreach (NetTarget t in fresh)
            {
                NetTarget old = netTargets.FirstOrDefault(o => o.Name == t.Name);
                if (old != null) { t.State = old.State; t.Ms = old.Ms; t.Error = old.Error; }
            }
            netTargets = fresh;
            netBar.Targets = netTargets;
            netBar.Visible = netTargets.Count > 0;
        }

        async Task CheckNet(bool manual)
        {
            if (netChecking || netTargets.Count == 0) return;
            netChecking = true;
            foreach (NetTarget t in netTargets) t.State = "checking";
            LayoutBar();
            UpdateOverlay();
            var tick = new System.Windows.Forms.Timer { Interval = 250 };
            tick.Tick += delegate { netBar.Invalidate(); };
            tick.Start();
            await Task.WhenAll(netTargets.Select(NetCheck.Check).ToArray());
            tick.Stop();
            netChecking = false;
            LayoutBar();
            Journal.Write("связь", string.Join(", ", NetSummary()));
            UpdateOverlay();
            var bad = netTargets.Where(t => t.State == "fail").ToList();
            if (bad.Count > 0)
            {
                string msg = "Нет связи: " + string.Join(", ", bad.Select(t => t.Name)) + ". Наведите на значок, чтобы увидеть причину.";
                Status(msg);
                if (!Visible) Notify("Нет связи: " + string.Join(", ", bad.Select(t => t.Name)), "Нажмите, чтобы открыть Старт-пак.", ShowFromTray);
            }
            else if (manual) Status("Связь в порядке: " + string.Join(", ", netTargets.Select(t => t.Name + (t.Ms < 0 ? " работает" : " " + t.Ms + " мс"))) + "." + ViaVpnNote());
        }

        string ViaVpnNote()
        {
            var on = cfg.Items.Where(v => VpnCtl.IsVpn(v) && VpnCtl.IsConnected(v, vpn)).Select(v => v.Name).ToList();
            return on.Count > 0 ? " (через VPN " + string.Join(", ", on) + " — без него может быть иначе)" : "";
        }

        List<string> NetSummary()
        {
            return netTargets.Select(t => t.Name + " " + (t.State == "ok" ? (t.Ms < 0 ? "ок" : "ок " + t.Ms + " мс") : t.State == "fail" ? "нет (" + t.Error + ")" : "не проверено")).ToList();
        }

        // ---------------------------------------------------------------- обновления

        async Task CheckUpdates(bool manual)
        {
            if (checking) return;
            var items = cfg.Items.Where(i => i.UpdateKind.Length > 0 && i.Repo.Trim().Length > 0).ToList();
            if (items.Count == 0) { if (manual) Status("Ни для одной программы не настроены обновления."); return; }
            checking = true;
            if (!runningAll) Status("Проверяю обновления на GitHub…");
            var cache = new Dictionary<string, Release>(StringComparer.OrdinalIgnoreCase);
            int fails = 0;
            foreach (Item it in items)
            {
                it.LocalVersion = Core.GetLocalVersion(it);
                it.CheckError = null;
                try
                {
                    Release r;
                    if (!cache.TryGetValue(it.Repo.Trim(), out r))
                    {
                        r = await Core.LatestRelease(it.Repo);
                        cache[it.Repo.Trim()] = r;
                    }
                    it.LatestTag = r.Tag;
                    it.LatestUrl = Core.PickAsset(it, r);
                    if (it.LatestUrl == null) it.CheckError = "в релизе нет файла " + (it.UpdateKind == "zapret" ? ".zip" : it.Asset);
                    Log("проверка " + it.Name + ": у вас " + it.LocalVersion + ", на GitHub " + r.Tag + (it.UpdateAvailable ? " — есть обновление" : ""));
                }
                catch (Exception ex)
                {
                    fails++;
                    it.LatestTag = null;
                    it.CheckError = "не удалось проверить";
                    Log("проверка " + it.Name + ": ошибка " + ex.Message);
                    Journal.Write("обновления", it.Name + ": не удалось проверить — " + ex.Message);
                }
                cards.Invalidate();
            }
            checking = false;
            RefreshState();
            var upd = cfg.Items.Where(i => i.UpdateAvailable).ToList();
            if (upd.Count > 0) Journal.Write("обновления", "есть: " + string.Join(", ", upd.Select(i => i.Name + " " + Core.CleanTag(i.LatestTag))));
            if (runningAll) return;
            if (upd.Count > 0)
                Status("Есть обновления: " + string.Join(", ", upd.Select(i => i.Name + " " + Core.CleanTag(i.LatestTag))) + ".");
            else if (fails > 0)
                Status("Не удалось проверить обновления — нет связи с GitHub? Запускать можно и так.");
            else
                Status("Всё обновлено. Нажмите «Запустить всё».");
        }

        async Task UpdateAll()
        {
            foreach (Item it in cfg.Items.Where(i => i.UpdateAvailable && Core.PathExists(i.Path)).ToList()) await UpdateItem(it);
            if (SelfUpdate.Last != null && SelfUpdate.Last.Newer) await RunSelfUpdate();   // сам Старт-пак — последним: он перезапустится
        }

        // ---------------------------------------------------------------- обновление самого Старт-пака

        string selfNotified;

        async Task CheckSelfUpdate(bool manual)
        {
            try
            {
                SelfUpdate.Info i = await SelfUpdate.Check();
                RefreshState();
                if (i != null && i.Newer)
                {
                    if (selfNotified != i.Version || manual)
                    {
                        selfNotified = i.Version;
                        Status("Вышла новая версия Старт-пака — " + i.Version + ". Кнопка «Обновить» вверху.");
                        Journal.Write("обновление", "доступен Старт-пак " + i.Version);
                        if (!Visible && cfg.UpdateNotify) Notify("Вышел Старт-пак " + i.Version, "Нажмите, чтобы открыть и обновить.", ShowFromTray);
                    }
                }
                else if (manual) Status(i == null ? "На GitHub пока нет выпусков Старт-пака." : "У вас последняя версия Старт-пака (" + Core.Version + ").");
            }
            catch (Exception ex)
            {
                if (manual) Status("Не удалось проверить обновления Старт-пака: " + ex.Message);
            }
        }

        async Task RunSelfUpdate()
        {
            SelfUpdate.Info i = SelfUpdate.Last;
            if (i == null || !i.Newer || i.ExeUrl == null)
            {
                if (i != null && i.ExeUrl == null) Tell("Обновление недоступно", "В выпуске " + i.Version + " нет файла StartPack.exe. Скачайте его вручную:\n" + SelfUpdate.ReleasesPage);
                return;
            }
            string notes = string.IsNullOrEmpty(i.Notes) ? "" : (i.Notes.Length > 600 ? i.Notes.Substring(0, 600) + "…" : i.Notes) + "\n\n";
            if (!Ask("Обновить Старт-пак до " + i.Version + "?", notes + "Старт-пак скачает новую версию с GitHub, проверит контрольную сумму и перезапустится. Список программ и настройки сохранятся.", "Обновить", "Позже")) return;
            string file = SelfUpdate.TempFile(i);
            try { await DownloadUi("Старт-пак " + i.Version, i.ExeUrl, file, null); }
            catch (OperationCanceledException) { Status("Обновление Старт-пака отменено."); return; }
            catch (Exception ex) { Tell("Не скачалось", ex.Message + "\n\nМожно скачать вручную: " + SelfUpdate.ReleasesPage); return; }
            string bad = SelfUpdate.Verify(file, i);
            if (bad != null)
            {
                try { File.Delete(file); } catch { }
                Journal.Write("обновление", "отказ: " + bad);
                Tell("Обновление не поставлено", Capital(bad) + ".\n\nСкачанный файл удалён. Новую версию можно взять вручную: " + SelfUpdate.ReleasesPage);
                return;
            }
            if (testLog != null) { Log("самообновление: файл проверен, запуск пропущен (самопроверка)"); return; }
            SelfUpdate.Launch(file);
            exiting = true;
            Close();
        }

        async Task UpdateItem(Item it)
        {
            if (it.Busy || !it.UpdateAvailable) return;
            it.Busy = true;
            it.Progress = 0;
            string ver = Core.CleanTag(it.LatestTag);
            Journal.Write("обновления", it.Name + ": скачиваю " + ver);
            try
            {
                string tmpRoot = Path.Combine(Path.GetTempPath(), "StartPack");
                Directory.CreateDirectory(tmpRoot);
                string dl = it.UpdateKind == "zapret"
                    ? Path.Combine(tmpRoot, "zapret-" + ver + ".zip")
                    : Core.Expand(it.Path) + ".download";
                SetMsg(it, "скачиваю " + ver + "…", false);
                await DownloadUi(it.Name + " " + ver, it.LatestUrl, dl, it);
                it.Busy = true;
                it.Progress = -1;
                Log("скачано " + it.Name + ": " + new FileInfo(dl).Length + " байт");
                if (it.UpdateKind == "zapret") await UpdateZapret(it, dl, ver);
                else await UpdateExe(it, dl, ver);
            }
            catch (OperationCanceledException)
            {
                it.Busy = false;
                SetMsg(it, "загрузка отменена — нажмите ещё раз, чтобы продолжить", false);
            }
            catch (Exception ex)
            {
                it.Busy = false;
                SetMsg(it, "обновление не удалось: " + ex.Message, true);
                Journal.Write("обновления", it.Name + ": не удалось — " + ex.Message);
                Log("ошибка обновления " + it.Name + ": " + ex);
            }
            finally
            {
                it.Busy = false;
                it.Progress = -1;
                RefreshState();
            }
        }

        async Task UpdateZapret(Item it, string zip, string ver)
        {
            string oldBat = Core.Expand(it.Path);
            string oldDir = Path.GetDirectoryName(oldBat);
            string bat = Path.GetFileName(oldBat);
            string parent = Path.GetDirectoryName(oldDir);
            string prefix = Core.ZapretPrefix(Path.GetFileName(oldDir)) ?? "zapret-discord-youtube-";
            SetMsg(it, "распаковываю…", false);
            string newDir = await Task.Run(() => Core.InstallZapret(zip, oldDir, Path.Combine(parent, prefix + ver)));
            it.Path = Path.Combine(newDir, File.Exists(Path.Combine(newDir, bat)) ? bat : "general.bat");
            it.LocalVersion = Core.GetLocalVersion(it);
            SaveCfg();
            it.Busy = false;
            SetMsg(it, "обновлён до " + ver, false);
            Status("zapret обновлён до " + ver + ". Ваши списки и настройки перенесены.");
            Journal.Write("обновления", "zapret " + ver + " поставлен в " + newDir);
            Log("zapret поставлен в " + newDir);

            if (Core.RunningNames().Contains("winws") &&
                Ask("Перезапустить zapret?", "zapret обновлён до " + ver + ". Перезапустить его сейчас на новой версии? Windows попросит права администратора.", "Перезапустить", "Позже"))
                await ZapretRestart(it);

            if (Cleanup.Scan(cfg).Any(c => c.Item == it && c.Kind == "zapret-old") &&
                Ask("Убрать старые версии?", "Старые папки zapret больше не нужны — ваши списки уже в новой. Открыть «Уборку», чтобы отправить их в корзину?", "Открыть уборку", "Позже"))
                ShowCleanup();
        }

        async Task UpdateExe(Item it, string dl, string ver)
        {
            string target = Core.Expand(it.Path);
            string[] names = Core.ProcNames(it);
            if (names.Length == 0) names = new[] { Path.GetFileNameWithoutExtension(target) };
            var procs = names.SelectMany(n => Process.GetProcessesByName(n)).ToList();
            bool wasRunning = procs.Count > 0;
            if (wasRunning)
            {
                if (!Ask("Закрыть «" + it.Name + "»?", "Программа сейчас работает. Закрыть её, чтобы заменить на версию " + ver + "? После замены она запустится снова.", "Закрыть и обновить", "Позже"))
                {
                    try { File.Delete(dl); } catch { }
                    it.Busy = false;
                    SetMsg(it, "обновление отложено", false);
                    return;
                }
                MarkExpected(it, true);
                foreach (Process p in procs)
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
                    p.Dispose();
                }
                await Task.Delay(700);
            }
            SetMsg(it, "заменяю файл…", false);
            await Task.Run(() => Core.ReplaceFile(dl, target));
            it.InstalledTag = ver;
            it.InstalledSize = new FileInfo(target).Length;
            it.LocalVersion = Core.GetLocalVersion(it);
            icons.Remove(it);
            SaveCfg();
            it.Busy = false;
            SetMsg(it, "обновлена до " + ver, false);
            Status("«" + it.Name + "» обновлена до " + ver + ".");
            Journal.Write("обновления", it.Name + " обновлена до " + ver);
            Log("заменён " + target + ", версия файла теперь " + it.LocalVersion);
            if (wasRunning)
            {
                try { Core.Launch(it); } catch (Exception ex) { SetMsg(it, "не запустилась: " + ex.Message, true); }
            }
        }

        async Task<Item> DownloadZapretFresh(Item card = null)
        {
            Status("Скачиваю zapret с GitHub…");
            Release r = await Core.LatestRelease("Flowseal/zapret-discord-youtube");
            var probe = new Item { UpdateKind = "zapret" };
            string url = Core.PickAsset(probe, r);
            if (url == null) throw new Exception("в последнем релизе zapret нет .zip");
            string ver = Core.CleanTag(r.Tag);
            string tmp = Path.Combine(Path.GetTempPath(), "StartPack");
            Directory.CreateDirectory(tmp);
            string zip = Path.Combine(tmp, "zapret-" + ver + ".zip");
            await DownloadUi("zapret " + ver, url, zip, card);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dir = await Task.Run(() => Core.InstallZapret(zip, null, Path.Combine(desktop, "zapret-discord-youtube-" + ver)));
            string bat = Path.Combine(dir, "general (ALT).bat");
            if (!File.Exists(bat)) bat = Path.Combine(dir, "general.bat");
            Status("zapret " + ver + " скачан в " + dir);
            Journal.Write("добавление", "скачан zapret " + ver + " в " + dir);
            return Templates.ZapretAt(bat);
        }

        async Task<Item> DownloadTgFresh(Item card = null)
        {
            Status("Скачиваю TG WS Proxy с GitHub…");
            Release r = await Core.LatestRelease("Flowseal/tg-ws-proxy");
            var probe = new Item { UpdateKind = "github-exe", Asset = "TgWsProxy_windows.exe" };
            string url = Core.PickAsset(probe, r);
            if (url == null) throw new Exception("в последнем релизе нет TgWsProxy_windows.exe");
            Directory.CreateDirectory(Templates.AppsDir);
            string dest = Path.Combine(Templates.AppsDir, "TgWsProxy_windows.exe");
            // во временную папку с версией в имени — чтобы докачка не склеила разные версии
            string tmpDir = Path.Combine(Path.GetTempPath(), "StartPack");
            Directory.CreateDirectory(tmpDir);
            string tmp = Path.Combine(tmpDir, "TgWsProxy-" + Core.CleanTag(r.Tag) + ".exe");
            await DownloadUi("TG WS Proxy " + Core.CleanTag(r.Tag), url, tmp, card);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            Item it = Templates.TgWsAt(dest);
            it.InstalledTag = Core.CleanTag(r.Tag);
            it.InstalledSize = new FileInfo(dest).Length;
            Status("TG WS Proxy " + it.InstalledTag + " скачан.");
            Journal.Write("добавление", "скачан TG WS Proxy " + it.InstalledTag);
            return it;
        }
    }
}
