// Логика без окон: запуск и остановка, VPN, пауза zapret на время VPN, установка программ, проверка связи, обновление.
// Окно только показывает состояние и вызывает эти методы — поэтому всё это проверяется самопроверкой без экрана.

namespace StartPack.Linux;

public static class Vpn
{
    // Happ: ядро (xray / sing-box) запускает служба happd из /opt/happ — значит, VPN подключён.
    public static bool HappConnected(List<ProcInfo> all) =>
        all.Any(p => (p.Comm is "sing-box" or "xray" || p.Exe.EndsWith("/sing-box") || p.Exe.EndsWith("/xray")) && p.CmdLine.Contains("/opt/happ/"));

    // WARP: при подключении появляется сетевой интерфейс CloudflareWARP.
    public static bool WarpConnected() => Directory.Exists("/sys/class/net/CloudflareWARP");

    public static bool Connected(Item it, List<ProcInfo> all) => it.Kind switch
    {
        "happ" => HappConnected(all),
        "warp" => WarpConnected(),
        _ => false
    };

    public static async Task<string> WarpCli(string cmd)
    {
        string cli = Sys.Which("warp-cli");
        if (cli == null) return "не найден warp-cli — Cloudflare WARP не установлен";
        var r = await Sys.Run(cli, new[] { "--accept-tos", cmd }, 20000);
        if (r.Output.Contains("Unable to connect to the CloudflareWARP daemon", StringComparison.OrdinalIgnoreCase))
            return "служба WARP не запущена: выполните «sudo systemctl enable --now warp-svc»";
        if (r.Output.Contains("Registration Missing", StringComparison.OrdinalIgnoreCase) || r.Output.Contains("Missing registration", StringComparison.OrdinalIgnoreCase))
            return "WARP ещё не настроен: выполните «warp-cli registration new» или откройте WARP один раз";
        return r.Code == 0 ? null : (r.Output.Trim().Split('\n').FirstOrDefault() ?? "warp-cli вернул ошибку");
    }
}

public class Engine
{
    public readonly Config Cfg;
    public List<ProcInfo> Procs = new();
    public List<NetTarget> Net = new();
    public bool NetChecking;
    public bool ZapretPausedForVpn;
    public Net.Release Update;                 // свежая версия Старт-пака, если есть
    public event Action Changed;
    public event Action<string> Status;

    public Engine(Config cfg)
    {
        Cfg = cfg;
        Net = StartPack.Linux.Net.TargetsFor(cfg);
    }

    public void Save() { try { Store.Save(Cfg); } catch (Exception ex) { Log.Write("настройки", "не сохранились: " + ex.Message); } }
    void Changed_() => Changed?.Invoke();
    void Say(string s) { Status?.Invoke(s); }

    public void Refresh()
    {
        Procs = Sys.Processes();
        bool any = false;
        foreach (Item it in Cfg.Items) if (string.IsNullOrEmpty(it.Command) || !Catalog.Exists(it)) any |= Catalog.Refresh(it);
        if (any) Save();
        Changed_();
    }

    public bool IsRunning(Item it) => it.Kind switch
    {
        "zapret" => Zapret.Running(Procs),
        "warp" => Sys.Which("warp-cli") != null && (Vpn.WarpConnected() || Sys.IsRunning(it.Process, Procs)),
        _ => !string.IsNullOrEmpty(it.Process) && Sys.IsRunning(it.Process, Procs)
    };

    public bool AnyVpnConnected() => Cfg.Items.Any(v => v.IsVpn && Vpn.Connected(v, Procs));

    void Msg(Item it, string m, bool err = false) { it.Message = m; it.MessageIsError = err; Changed_(); }

    // ---------------------------------------------------------------- запуск

    // 0 — запущена, 1 — уже работала, 2 — ошибка
    public async Task<int> RunOne(Item it, bool inSequence)
    {
        Refresh();
        if (!Catalog.Exists(it)) { Msg(it, "не установлена — нажмите «Установить»", true); return 2; }
        if (it.Kind == "zapret") return await StartZapret(it);
        if (it.IsVpn) return await RunVpn(it);
        if (IsRunning(it)) { Msg(it, null); return 1; }
        try
        {
            Launch(it);
            Log.Write("запуск", it.Name);
            it.Busy = true; Msg(it, "запускаю…");
            for (int i = 0; i < 60 && !Sys.IsRunning(it.Process); i++) await Task.Delay(250);
            it.Busy = false;
            Refresh();
            Msg(it, IsRunning(it) ? null : "запущена, но процесс не появился", false);
            if (inSequence && it.Delay > 0) await Task.Delay(it.Delay * 1000);
            return 0;
        }
        catch (Exception ex)
        {
            it.Busy = false;
            Msg(it, "не запустилась: " + ex.Message, true);
            Log.Write("запуск", it.Name + ": " + ex.Message);
            return 2;
        }
    }

    static void Launch(Item it)
    {
        if (it.Command.StartsWith("flatpak:"))
            Sys.Launch(Sys.Which("flatpak") ?? "flatpak", new[] { "run", it.Command[8..] }.Concat(Sys.SplitArgs(it.Args)));
        else
            Sys.Launch(Sys.Which(it.Command) ?? it.Command, Sys.SplitArgs(it.Args));
    }

    public async Task RunAll()
    {
        int started = 0, already = 0, errors = 0;
        Item skipped = null;
        bool vpnComing = Cfg.PauseZapretForVpn && Cfg.Items.Any(v => v.Enabled && v.IsVpn && v.AutoConnect && Catalog.Exists(v));
        Log.Write("запуск", "«Запустить всё»");
        foreach (Item it in Cfg.Items.ToList())
        {
            if (!it.Enabled) continue;
            if (vpnComing && it.Kind == "zapret" && !Zapret.Running())
            {
                // вместе с VPN zapret ломает Discord и YouTube — его место займёт VPN
                skipped = it; ZapretPausedForVpn = true;
                Msg(it, "не запущен: подключается VPN");
                continue;
            }
            int r = await RunOne(it, true);
            if (r == 0) started++; else if (r == 1) already++; else errors++;
        }
        if (skipped != null)
        {
            Refresh();
            if (!AnyVpnConnected())
            {
                ZapretPausedForVpn = false;
                Log.Write("запуск", "VPN не подключился — запускаю zapret");
                int r = await RunOne(skipped, true);
                if (r == 0) started++; else errors++;
            }
            else Msg(skipped, "на паузе, пока подключён VPN");
        }
        var parts = new List<string>();
        if (started > 0) parts.Add("запущено: " + started);
        if (already > 0) parts.Add("уже работали: " + already);
        if (errors > 0) parts.Add("ошибок: " + errors);
        Say(parts.Count == 0 ? "В «Запустить всё» нет ни одной программы — включите переключатели на карточках." : "Готово — " + string.Join(", ", parts) + ".");
        if (Cfg.NetCheck && (started > 0 || errors > 0)) { await Task.Delay(2500); await CheckNet(); }
    }

    public async Task<bool> StopOne(Item it)
    {
        if (it.Kind == "zapret")
        {
            it.Busy = true; Msg(it, "останавливаю…");
            string err = await Zapret.Stop();
            it.Busy = false; Refresh();
            Msg(it, err == null ? "остановлен" : err, err != null);
            return err == null;
        }
        if (it.IsVpn && Vpn.Connected(it, Procs)) await Disconnect(it);
        it.Busy = true; Msg(it, "закрываю…");
        bool ok = await Sys.Stop(it.Process);
        it.Busy = false; Refresh();
        Msg(it, ok ? "остановлена" : "не закрылась (возможно, запущена от администратора)", !ok);
        Log.Write("стоп", it.Name + (ok ? ": остановлена" : ": не закрылась"));
        return ok;
    }

    // ---------------------------------------------------------------- zapret

    public async Task<int> StartZapret(Item it, bool restart = false)
    {
        if (Zapret.Running(Procs) && !restart) { Msg(it, null); return 1; }
        if (Cfg.PauseZapretForVpn && AnyVpnConnected())
        {
            ZapretPausedForVpn = true;
            Msg(it, "на паузе, пока подключён VPN");
            return 1;
        }
        it.Busy = true; Msg(it, "запускаю — система спросит пароль…");
        string err = await Zapret.Start(Cfg.Zapret);
        it.Busy = false; Save(); Refresh();
        Msg(it, err == null ? null : err, err != null);
        return err == null ? 0 : 2;
    }

    public async Task SwitchStrategy(Item it, string bat)
    {
        Cfg.Zapret.Strategy = bat;
        Save();
        Log.Write("zapret", "стратегия → " + Zapret.Pretty(bat));
        if (Zapret.Running()) await StartZapret(it, true);
        else Say("Стратегия «" + Zapret.Pretty(bat) + "» включится при следующем запуске zapret.");
    }

    async Task PauseZapretForVpn(Item vpn)
    {
        Item z = Cfg.Items.FirstOrDefault(i => i.Kind == "zapret");
        if (!Cfg.PauseZapretForVpn || z == null || !Zapret.Running()) return;
        Say("Подключён " + vpn.Name + " — останавливаю zapret: вместе они ломают Discord и YouTube.");
        if (await StopOne(z)) { ZapretPausedForVpn = true; Msg(z, "на паузе, пока подключён VPN"); }
    }

    async Task ResumeZapretAfterVpn()
    {
        if (!ZapretPausedForVpn) return;
        Refresh();
        if (AnyVpnConnected()) return;
        ZapretPausedForVpn = false;
        Item z = Cfg.Items.FirstOrDefault(i => i.Kind == "zapret");
        if (z == null || Zapret.Running()) return;
        Say("VPN отключён — запускаю zapret снова.");
        await StartZapret(z);
    }

    // ---------------------------------------------------------------- VPN

    async Task<int> RunVpn(Item it)
    {
        bool open = IsRunning(it);
        if (it.Kind == "warp")
        {
            if (!open && Sys.Which("warp-taskbar") != null) { try { Sys.Launch(Sys.Which("warp-taskbar"), Array.Empty<string>()); } catch { } }
            if (it.AutoConnect && !Vpn.WarpConnected()) return await Connect(it) ? 0 : 2;
            Msg(it, null);
            return open ? 1 : 0;
        }
        // Happ: подключить снаружи на Linux нельзя — открыть окно и подсказать
        if (!open)
        {
            try { Launch(it); } catch (Exception ex) { Msg(it, "не запустился: " + ex.Message, true); return 2; }
            Log.Write("запуск", it.Name);
            for (int i = 0; i < 40 && !Sys.IsRunning(it.Process); i++) await Task.Delay(250);
        }
        Refresh();
        if (Vpn.HappConnected(Procs)) { Msg(it, null); await PauseZapretForVpn(it); return open ? 1 : 0; }
        Msg(it, "открыт — нажмите «Подключиться» в окне Happ");
        return open ? 1 : 0;
    }

    public async Task<bool> Connect(Item it)
    {
        if (it.Kind == "happ")
        {
            Msg(it, "подключитесь в окне Happ — снаружи его подключить нельзя");
            if (!IsRunning(it)) { try { Launch(it); } catch { } }
            return false;
        }
        it.Busy = true; Msg(it, "подключаю…");
        string err = await Vpn.WarpCli("connect");
        bool ok = false;
        if (err == null) for (int i = 0; i < 30 && !(ok = Vpn.WarpConnected()); i++) await Task.Delay(500);
        it.Busy = false; Refresh();
        Msg(it, ok ? null : "не подключился: " + (err ?? "нет ответа от WARP"), !ok);
        Log.Write("warp", ok ? "подключён" : "не подключился: " + err);
        if (ok) await PauseZapretForVpn(it);
        return ok;
    }

    public async Task Disconnect(Item it)
    {
        if (it.Kind == "happ") { Msg(it, "отключите VPN в окне Happ"); return; }
        it.Busy = true; Msg(it, "отключаю…");
        string err = await Vpn.WarpCli("disconnect");
        bool off = false;
        if (err == null) for (int i = 0; i < 20 && !(off = !Vpn.WarpConnected()); i++) await Task.Delay(500);
        it.Busy = false; Refresh();
        Msg(it, off ? null : "не отключился: " + (err ?? "нет ответа от WARP"), !off);
        Log.Write("warp", off ? "отключён" : "не отключился: " + err);
        if (off) await ResumeZapretAfterVpn();
    }

    // Раз в несколько секунд: VPN подключили не из Старт-пака, а zapret работает. true — предупредить один раз.
    bool conflictShown;
    public bool ConflictToWarn()
    {
        bool both = Zapret.Running(Procs) && AnyVpnConnected();
        if (!both) { conflictShown = false; return false; }
        if (conflictShown) return false;
        conflictShown = true;
        return Cfg.PauseZapretForVpn;
    }

    public async Task StopZapretForVpn()
    {
        Item z = Cfg.Items.FirstOrDefault(i => i.Kind == "zapret");
        if (z != null && await StopOne(z)) { ZapretPausedForVpn = true; Msg(z, "на паузе, пока подключён VPN"); }
    }

    // ---------------------------------------------------------------- связь

    public async Task CheckNet()
    {
        if (NetChecking) return;
        Net = StartPack.Linux.Net.TargetsFor(Cfg);
        if (Net.Count == 0) return;
        NetChecking = true; Changed_();
        await Task.WhenAll(Net.Select(StartPack.Linux.Net.Check));
        NetChecking = false;
        Log.Write("связь", string.Join(", ", Net.Select(t => t.Name + " " + (t.State == "ok" ? (t.Ms >= 0 ? t.Ms + " мс" : "ок") : "нет (" + t.Error + ")"))));
        var bad = Net.Where(t => t.State == "fail").Select(t => t.Name).ToList();
        Say(bad.Count > 0 ? "Нет связи: " + string.Join(", ", bad) + ". Наведите на значок, чтобы увидеть причину."
            : "Связь в порядке: " + string.Join(", ", Net.Select(t => t.Name + (t.Ms >= 0 ? " " + t.Ms + " мс" : " работает"))) + ".");
        Changed_();
    }

    // ---------------------------------------------------------------- установка

    public async Task<string> Install(Item it, Action<string> progress)
    {
        Known k = Catalog.Get(it.Kind);
        if (k == null) return "это своя программа — установите её сами";
        var os = Sys.Os();
        string err;
        try
        {
            if (k.Kind == "zapret") err = await Zapret.Install(progress);
            else if (k.Flatpak != null && Sys.HasFlatpak)
            {
                progress("ставлю из Flathub…");
                await Sys.Run("flatpak", new[] { "remote-add", "--user", "--if-not-exists", "flathub", "https://dl.flathub.org/repo/flathub.flatpakrepo" }, 60000);
                var r = await Sys.Run("flatpak", new[] { "install", "--user", "-y", "--noninteractive", "flathub", k.Flatpak }, 1800000);
                err = r.Code == 0 ? null : "Flathub не поставил программу: " + LastLine(r.Output);
            }
            else if (k.BinAsset != null)
            {
                progress("скачиваю с GitHub…");
                var rel = await StartPack.Linux.Net.Latest(k.GithubRepo);
                var a = rel.Assets.FirstOrDefault(x => x.Name == k.BinAsset) ?? throw new Exception("в выпуске нет " + k.BinAsset);
                string dir = Path.Combine(Store.DataDir, "apps");
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, "tg-ws-proxy");
                await StartPack.Linux.Net.DownloadVerified(a, dst + ".download", new Progress<int>(p => progress("скачиваю " + p + "%")));
                File.Move(dst + ".download", dst, true);
                File.SetUnixFileMode(dst, (UnixFileMode)0b111_101_101);
                err = null;
            }
            else if (k.GithubRepo != null && os.PackageManager != null)
            {
                string asset = os.PackageManager == "apt" ? k.DebAsset : k.RpmAsset;
                progress("скачиваю пакет с GitHub…");
                var rel = await StartPack.Linux.Net.Latest(k.GithubRepo);
                var a = rel.Assets.FirstOrDefault(x => x.Name == asset) ?? throw new Exception("в выпуске нет " + asset);
                string file = Path.Combine(Path.GetTempPath(), asset);
                await StartPack.Linux.Net.DownloadVerified(a, file, new Progress<int>(p => progress("скачиваю " + p + "%")));
                progress("устанавливаю — система спросит пароль…");
                var r = os.PackageManager == "apt"
                    ? await Sys.RunElevated("/usr/bin/env", new[] { "DEBIAN_FRONTEND=noninteractive", "apt-get", "install", "-y", file })
                    : await Sys.RunElevated("/usr/bin/env", new[] { "dnf", "install", "-y", file });
                try { File.Delete(file); } catch { }
                err = r.Code == 0 ? null : "пакет не установился: " + LastLine(r.Output);
            }
            else if (k.AptPackage != null && os.PackageManager == "apt")
            {
                progress("ставлю из репозитория системы — система спросит пароль…");
                var r = await Sys.RunElevated("/usr/bin/env", new[] { "DEBIAN_FRONTEND=noninteractive", "apt-get", "install", "-y", k.AptPackage });
                err = r.Code == 0 ? null : "пакет не установился: " + LastLine(r.Output);
            }
            else err = "page";
        }
        catch (Exception ex) { err = ex.Message; }
        if (err == null)
        {
            Catalog.Refresh(it);
            Save();
            Log.Write("установка", it.Name + " установлена");
        }
        else if (err != "page") Log.Write("установка", it.Name + ": " + err);
        Refresh();
        return err;
    }

    static string LastLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";

    // ---------------------------------------------------------------- обновление Старт-пака

    public const string ProjectRepo = "hTTT9/StartPack";
    public const string AppImageAsset = "StartPack-x86_64.AppImage";

    public async Task CheckUpdate()
    {
        try
        {
            var rel = await StartPack.Linux.Net.LatestWithPrefix(ProjectRepo, "linux-v");
            Update = rel != null && StartPack.Linux.Net.CompareVersions(rel.Tag, App.Version) > 0 ? rel : null;
            if (Update != null) Say("Вышла новая версия Старт-пака для Linux — " + Update.Tag.Replace("linux-v", "") + ". Кнопка «Обновить» вверху.");
            Changed_();
        }
        catch (Exception ex) { Log.Write("обновление", "не проверилось: " + ex.Message); }
    }

    // AppImage обновляется сам; пакеты .deb и .rpm — через страницу выпуска.
    public static bool IsAppImage => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

    public async Task<string> ApplyUpdate(Action<string> progress)
    {
        if (Update == null) return "обновлений нет";
        if (!IsAppImage) { Sys.Open(Update.Page); return "page"; }
        var a = Update.Assets.FirstOrDefault(x => x.Name == AppImageAsset);
        if (a == null) return "в выпуске нет " + AppImageAsset;
        string self = Environment.GetEnvironmentVariable("APPIMAGE");
        string tmp = self + ".new";
        await StartPack.Linux.Net.DownloadVerified(a, tmp, new Progress<int>(p => progress("скачиваю " + p + "%")));
        File.SetUnixFileMode(tmp, (UnixFileMode)0b111_101_101);
        File.Move(tmp, self, true);
        Log.Write("обновление", "поставлена " + Update.Tag);
        Sys.Launch(self, new[] { "--updated" });
        return null;
    }
}
