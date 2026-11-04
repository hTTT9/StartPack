// Самопроверка без окна (для GitHub Actions на Ubuntu, Debian и Fedora) и снимки окон без экрана.
// Всё пишется во временные папки (XDG_CONFIG_HOME / XDG_DATA_HOME) — настоящий список человека не трогается.
//   STARTPACK_TEST_NET=1     — проверки с интернетом: связь, скачивание TG WS Proxy с проверкой SHA-256, выпуски GitHub
//   STARTPACK_TEST_ZAPRET=1  — полный цикл zapret: установка, служба, nfqws, правила nftables, смена стратегии, остановка
//                              (нужен sudo без пароля: STARTPACK_ELEVATE=sudo)

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace StartPack.Linux;

public static class SelfTest
{
    static int failed, passed;
    static StreamWriter log;

    static void Ok(bool cond, string what, string detail = null)
    {
        string line = (cond ? "PASS  " : "FAIL  ") + what + (detail != null ? " — " + detail : "");
        if (cond) passed++; else failed++;
        Console.WriteLine(line);
        log.WriteLine(line);
        log.Flush();
    }

    static void Note(string s) { Console.WriteLine("      " + s); log.WriteLine("      " + s); log.Flush(); }

    static bool Env(string n) => Environment.GetEnvironmentVariable(n) == "1";

    public static int Run(string logFile)
    {
        // В домашней папке, как у людей (а не в /tmp): так видно, если службе или nfqws не хватает прав на путь в ~.
        string home = Environment.GetEnvironmentVariable("HOME");
        string baseDir = !string.IsNullOrEmpty(home) && Directory.Exists(home) ? Path.Combine(home, ".cache") : Path.GetTempPath();
        string root = Path.Combine(baseDir, "startpack-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
        log = new StreamWriter(logFile) { AutoFlush = true };
        try { RunAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { Ok(false, "самопроверка упала", ex.ToString()); }
        Note("итог: прошло " + passed + ", не прошло " + failed);
        log.Dispose();
        try { Directory.Delete(root, true); } catch { }
        return failed == 0 ? 0 : 1;
    }

    static async Task RunAsync()
    {
        Note("Старт-пак для Linux " + App.Version + " — самопроверка");
        var os = Sys.Os();
        Note("система: " + os.Name + " (id " + os.Id + ", like " + os.IdLike + "), менеджер пакетов: " + (os.PackageManager ?? "-"));

        // ---- os-release
        string fake = Path.GetTempFileName();
        File.WriteAllText(fake, "NAME=\"RED OS\"\nID=redos\nID_LIKE=\"rhel centos fedora\"\nVERSION_ID=8\nPRETTY_NAME=\"RED OS 8\"\n");
        var red = Sys.Os(fake);
        Ok(red.IsFedoraLike && red.PackageManager == "dnf", "РЕД ОС распознаётся как dnf-система");
        File.WriteAllText(fake, "ID=ubuntu\nID_LIKE=debian\nPRETTY_NAME=\"Ubuntu 24.04\"\n");
        Ok(Sys.Os(fake).PackageManager == "apt", "Ubuntu распознаётся как apt-система");
        File.Delete(fake);

        // ---- настройки
        Config c = Store.Load();
        Ok(Store.FirstRun && c.Items.Count == Catalog.All.Count, "первый запуск — стандартный набор из " + Catalog.All.Count + " программ");
        Ok(c.Items.Where(i => i.IsVpn).All(i => !i.Enabled), "VPN в новом списке выключены");
        c.Items.Add(new Item { Name = "Тест", Command = "/bin/sleep", Args = "30", Process = "sleep" });
        Store.Save(c);
        Config back = Store.Load();
        Ok(back.Items.Count == c.Items.Count && back.Items.Last().Args == "30", "список сохраняется и читается обратно");

        // ---- стратегии
        Ok(Zapret.Pretty("general_alt2.bat") == "ALT2" && Zapret.Pretty("general_fake_tls_auto.bat") == "FAKE TLS AUTO" && Zapret.Pretty("general.bat") == "general",
            "названия стратегий: general_alt2.bat → ALT2");

        if (!OperatingSystem.IsLinux()) { Note("не Linux — проверки системы пропущены"); return; }

        // ---- процессы, запуск и остановка своей программы
        var e = new Engine(back);
        Item t = back.Items.Last();
        int r = await e.RunOne(t, false);
        Ok(r == 0 && e.IsRunning(t), "своя программа запускается и видна по процессу", "код " + r);
        bool stopped = await e.StopOne(t);
        Ok(stopped && !e.IsRunning(t), "кнопка «Остановить» закрывает программу");
        Ok(Sys.SplitArgs("-a \"b c\" d").SequenceEqual(new[] { "-a", "b c", "d" }), "параметры с кавычками разбираются правильно");

        // ---- автозапуск
        Sys.SetAutostart(true);
        string desktop = File.Exists(Sys.AutostartFile) ? File.ReadAllText(Sys.AutostartFile) : "";
        Ok(desktop.Contains("--autorun") && desktop.Contains("[Desktop Entry]"), "автозапуск: файл в ~/.config/autostart", Sys.AutostartFile);
        Sys.SetAutostart(false);
        Ok(!Sys.AutostartEnabled, "автозапуск выключается");

        // ---- поиск программ
        foreach (Known k in Catalog.All) Note("найдено: " + k.Name + " → " + (Catalog.Detect(k) ?? "не установлена"));
        Note("права администратора: " + (Sys.ElevatePrefix() is string[] p ? (p.Length == 0 ? "уже root" : string.Join(" ", p)) : "нет pkexec и sudo"));

        // ---- интернет
        if (Env("STARTPACK_TEST_NET"))
        {
            var targets = new List<NetTarget> { new() { Name = "YouTube", Url = "https://www.youtube.com/generate_204" }, new() { Name = "Discord", Url = "https://discord.com/api/v9/gateway" } };
            await Task.WhenAll(targets.Select(Net.Check));
            foreach (var x in targets) Note("связь " + x.Name + ": " + x.State + " " + x.Ms + " мс " + x.Error);
            Ok(targets.All(x => x.State == "ok"), "проверка связи отвечает");

            Item tg = back.Items.First(i => i.Kind == "tgws");
            string err = await e.Install(tg, s => Note("tgws: " + s));
            string bin = Path.Combine(Store.DataDir, "apps", "tg-ws-proxy");
            Ok(err == null && File.Exists(bin) && (File.GetUnixFileMode(bin) & UnixFileMode.UserExecute) != 0, "TG WS Proxy скачивается с GitHub, SHA-256 сходится, файл исполняемый", err);
            Ok(tg.Command == bin, "карточка TG WS Proxy после установки указывает на скачанный файл", tg.Command);

            var rel = await Net.LatestWithPrefix("hTTT9/StartPack", "linux-v");
            Note("выпуск Старт-пака для Linux на GitHub: " + (rel?.Tag ?? "пока нет"));
            Ok(Net.CompareVersions("linux-v1.0.1", "1.0.0") > 0 && Net.CompareVersions("1.0.0", "1.0") == 0, "сравнение версий");
        }

        // ---- zapret: полный цикл
        if (Env("STARTPACK_TEST_ZAPRET"))
        {
            Item z = back.Items.First(i => i.Kind == "zapret");
            string err = await e.Install(z, s => Note("zapret: " + s));
            Ok(err == null && Zapret.Installed, "zapret для Linux ставится (стратегии + nfqws)", err);
            var strategies = Zapret.Strategies();
            Note("стратегий: " + strategies.Count + " — " + string.Join(", ", strategies.Take(8).Select(Zapret.Pretty)));
            Ok(strategies.Contains("general_alt.bat"), "есть стратегия ALT (general_alt.bat)");
            if (err != null || !Zapret.Installed || strategies.Count == 0) { Note("zapret не установился — дальше проверять нечего"); return; }

            back.Zapret.Strategy = "general_alt.bat";
            int zr = await e.RunOne(z, false);
            Ok(zr == 0 && Zapret.Running(), "zapret запускается службой, nfqws работает", z.Message);
            if (!Zapret.Running()) await Journal();
            var nft = await Sys.RunElevated("nft", new[] { "list", "table", "inet", "zapretunix" }, 20000);
            Ok(nft.Code == 0 && nft.Output.Contains("queue"), "правила nftables на месте (таблица zapretunix)");
            string conf = File.Exists(Zapret.ConfFile) ? File.ReadAllText(Zapret.ConfFile) : "";
            Ok(conf.Contains("strategy=general_alt.bat"), "conf.env записан");

            // Пакеты идут через nfqws: если он их не отпускает, сайты перестают открываться. Проверяем, что интернет жив.
            foreach (string url in new[] { "https://discord.com", "https://www.youtube.com", "https://example.com" })
            {
                var c2 = await Sys.Run("curl", new[] { "-sS", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "20", url }, 30000);
                Ok(c2.Code == 0 && c2.Output.Trim().Length == 3 && c2.Output.Trim() != "000", "при включённом zapret открывается " + url, "код " + c2.Output.Trim());
            }
            await Journal();

            string alt2 = strategies.Contains("general_alt2.bat") ? "general_alt2.bat" : strategies.First(s => s != "general_alt.bat");
            await e.SwitchStrategy(z, alt2);
            Ok(Zapret.Running() && File.ReadAllText(Zapret.ConfFile).Contains("strategy=" + alt2), "смена стратегии перезапускает zapret", Zapret.Pretty(alt2));
            var ps = Sys.Find("nfqws").FirstOrDefault();
            Note("nfqws: " + (ps?.CmdLine ?? "-"));

            bool zs = await e.StopOne(z);
            var nft2 = await Sys.RunElevated("nft", new[] { "list", "table", "inet", "zapretunix" }, 20000);
            Ok(zs && !Zapret.Running(), "zapret останавливается", z.Message);
            Ok(nft2.Code != 0, "после остановки правила nftables убраны");

            var rm = await Sys.RunElevated("/usr/bin/env", new[] { "bash", Zapret.Script, "service", "remove" }, 60000, Zapret.Dir);
            Ok(rm.Code == 0 && !Zapret.ServiceInstalled, "служба zapret удаляется");
        }
    }

    // Последние строки журнала службы — чтобы по логу GitHub было видно, что говорил nfqws.
    static async Task Journal()
    {
        if (Sys.Which("journalctl") == null) return;
        var j = await Sys.RunElevated("journalctl", new[] { "-u", "zapret_discord_youtube", "-n", "25", "--no-pager", "-o", "cat" }, 20000);
        foreach (string l in j.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Note("журнал: " + l);
    }
}

// Снимки окон без экрана (Avalonia Headless + Skia) — те же, что увидит человек.
public static class Shots
{
    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        string root = Path.Combine(Path.GetTempPath(), "startpack-shots-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));

        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();

        foreach (string theme in new[] { "dark", "light" })
        {
            App.Cfg = Store.Load();
            App.Cfg.Theme = theme;
            Pal.Apply(Application.Current!, theme);
            var e = new Engine(App.Cfg);
            // как будет выглядеть после проверки связи
            foreach (var t in e.Net) { t.State = "ok"; t.Ms = t.Name switch { "YouTube" => 51, "Discord" => 43, "Spotify" => 72, _ => -1 }; }
            var w = new MainWindow(e) { Width = 1000, Height = 860 };
            w.Show();
            Save(w, Path.Combine(dir, "main-" + theme + ".png"));
            w.Close();
        }
        Console.WriteLine("снимки: " + dir);
        try { Directory.Delete(root, true); } catch { }
        return 0;
    }

    static void Save(Window w, string file)
    {
        for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        var frame = w.CaptureRenderedFrame();
        frame?.Save(file);
    }
}
