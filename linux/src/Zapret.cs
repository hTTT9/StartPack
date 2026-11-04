// zapret для Linux — перенос стратегий Flowseal (github.com/Sergeydigl3/zapret-discord-youtube-linux).
// Старт-пак держит его в своей папке (~/.local/share/startpack/zapret) и управляет через его же service.sh:
// conf.env (стратегия, интерфейс, игровой фильтр) + системная служба zapret_discord_youtube (нужны права администратора).

using System.Text.RegularExpressions;

namespace StartPack.Linux;

public static class Zapret
{
    public const string Repo = "Sergeydigl3/zapret-discord-youtube-linux";

    // Проверенная версия переноса (master, 17.08.2026). Новые версии — только вместе с обновлением Старт-пака,
    // чтобы чужая правка не сломала установку у людей. Стратегии Flowseal и nfqws этот перенос тоже закрепляет у себя.
    public const string Rev = "69db771b527ba11ca71efe28728a7f9491eec352";

    // Своя папка Старт-пака; если человек уже клонировал перенос в домашнюю папку — берём его.
    public static string Dir
    {
        get
        {
            string own = Path.Combine(Store.DataDir, "zapret");
            if (IsComplete(own)) return own;
            foreach (string d in new[] { Path.Combine(Store.Home, "zapret-discord-youtube-linux"), Path.Combine(Store.Home, "zapret-discord-youtube-linux-main") })
                if (IsComplete(d)) return d;
            return own;
        }
    }

    static bool IsComplete(string d) =>
        File.Exists(Path.Combine(d, "service.sh")) && Directory.Exists(Path.Combine(d, "zapret-latest")) && File.Exists(Path.Combine(d, "nfqws"));

    public static bool Installed => IsComplete(Dir);
    public static string Script => Path.Combine(Dir, "service.sh");
    public static bool Running(List<ProcInfo> all = null) => Sys.IsRunning("nfqws", all);

    // Стратегии — те же файлы, что у Flowseal, но с именами вида general_alt2.bat.
    public static List<string> Strategies()
    {
        var list = new List<string>();
        string repo = Path.Combine(Dir, "zapret-latest"), custom = Path.Combine(Dir, "custom-strategies");
        if (Directory.Exists(repo))
            list.AddRange(Directory.GetFiles(repo, "*.bat").Select(Path.GetFileName).Where(n => n.StartsWith("general") || n.StartsWith("discord")));
        if (Directory.Exists(custom))
            list.AddRange(Directory.GetFiles(custom, "*.bat").Select(Path.GetFileName));
        return list.Distinct().OrderBy(n => Regex.Replace(n, @"\d+", m => m.Value.PadLeft(6, '0'))).ToList();
    }

    // general_alt2.bat → «ALT2», general_fake_tls_auto.bat → «FAKE TLS AUTO», general.bat → «general»
    public static string Pretty(string bat)
    {
        string n = Path.GetFileNameWithoutExtension(bat ?? "");
        if (n.Equals("general", StringComparison.OrdinalIgnoreCase)) return "general";
        if (n.StartsWith("general_", StringComparison.OrdinalIgnoreCase)) n = n[8..];
        return n.Replace('_', ' ').ToUpperInvariant();
    }

    public static string ConfFile => Path.Combine(Dir, "conf.env");

    public static void WriteConfig(ZapretSettings s)
    {
        File.WriteAllText(ConfFile,
            "strategy=" + s.Strategy + "\n" +
            "interface=" + (string.IsNullOrWhiteSpace(s.Interface) ? "any" : s.Interface) + "\n" +
            "gamefiltertcp=" + (s.GameFilterTcp ? "true" : "false") + "\n" +
            "gamefilterudp=" + (s.GameFilterUdp ? "true" : "false") + "\n");
    }

    // Сетевые интерфейсы для настройки (кроме lo).
    public static List<string> Interfaces()
    {
        var res = new List<string> { "any" };
        try { res.AddRange(Directory.GetDirectories("/sys/class/net").Select(Path.GetFileName).Where(n => n != "lo").OrderBy(n => n)); } catch { }
        return res;
    }

    // ---------------------------------------------------------------- служба

    public static bool ServiceInstalled
    {
        get
        {
            if (File.Exists("/etc/systemd/system/zapret_discord_youtube.service")) return true;
            return File.Exists("/etc/init.d/zapret_discord_youtube") || Directory.Exists("/etc/sv/zapret_discord_youtube") ||
                   Directory.Exists("/etc/s6/sv/zapret_discord_youtube") || File.Exists("/etc/dinit.d/zapret_discord_youtube");
        }
    }

    // Запустить (или перезапустить с новой стратегией). Ставит службу при первом запуске — тогда zapret стартует и при загрузке.
    public static async Task<string> Start(ZapretSettings s)
    {
        if (!Installed) return "zapret ещё не установлен — нажмите «Установить» на карточке";
        if (!Strategies().Contains(s.Strategy))
        {
            string first = Strategies().FirstOrDefault(n => n == "general_alt.bat") ?? Strategies().FirstOrDefault();
            if (first == null) return "в папке zapret нет стратегий — переустановите его";
            s.Strategy = first;
        }
        WriteConfig(s);
        bool inst = ServiceInstalled;
        Log.Write("zapret", (inst ? "перезапуск службы" : "установка службы") + ", стратегия " + Pretty(s.Strategy));
        var r = await Sys.RunElevated("/usr/bin/env", new[] { "bash", Script, "service", inst ? "restart" : "install" }, 120000, Dir);
        if (r.Code != 0) return Short(r);
        for (int i = 0; i < 40 && !Running(); i++) await Task.Delay(250);
        return Running() ? null : "служба запустилась, но процесс nfqws не появился — подробности в журнале";
    }

    public static async Task<string> Stop()
    {
        if (!Installed) return null;
        Log.Write("zapret", "остановка");
        string[] cmd = ServiceInstalled ? new[] { "bash", Script, "service", "stop" } : new[] { "bash", Script, "kill" };
        var r = await Sys.RunElevated("/usr/bin/env", cmd, 60000, Dir);
        if (r.Code != 0) return Short(r);
        for (int i = 0; i < 40 && Running(); i++) await Task.Delay(250);
        return Running() ? "zapret не остановился" : null;
    }

    static string Short(Sys.RunResult r)
    {
        Log.Write("zapret", "ошибка (код " + r.Code + "): " + r.Output);
        string last = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return r.Code is 126 or 127 or -2 ? r.Output : "не получилось (код " + r.Code + ")" + (last.Length > 0 ? ": " + last : "");
    }

    // ---------------------------------------------------------------- установка

    // Чего не хватает переносу zapret: git, curl, nft.
    public static List<string> MissingTools() =>
        new[] { ("git", "git"), ("curl", "curl"), ("nft", "nftables") }.Where(t => Sys.Which(t.Item1) == null).Select(t => t.Item2).ToList();

    public static async Task<string> Install(Action<string> progress)
    {
        var missing = MissingTools();
        if (missing.Count > 0)
        {
            var os = Sys.Os();
            if (os.PackageManager == null) return "не хватает программ: " + string.Join(", ", missing) + ". Установите их через менеджер пакетов системы";
            progress("ставлю " + string.Join(", ", missing) + "…");
            var r = os.PackageManager == "apt"
                ? await Sys.RunElevated("/usr/bin/env", new[] { "DEBIAN_FRONTEND=noninteractive", "apt-get", "install", "-y" }.Concat(missing), 900000)
                : await Sys.RunElevated("/usr/bin/env", new[] { "dnf", "install", "-y" }.Concat(missing), 900000);
            if (r.Code != 0) return "не поставились " + string.Join(", ", missing) + ": " + r.Output.Split('\n').LastOrDefault(l => l.Trim().Length > 0);
        }

        string own = Path.Combine(Store.DataDir, "zapret");
        string tmp = Path.Combine(Path.GetTempPath(), "startpack-zapret-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            progress("скачиваю zapret для Linux…");
            string tgz = Path.Combine(tmp, "src.tar.gz");
            await Net.Download("https://github.com/" + Repo + "/archive/" + Rev + ".tar.gz", tgz);
            var x = await Sys.Run("tar", new[] { "-xzf", tgz, "-C", tmp });
            if (x.Code != 0) return "архив не распаковался: " + x.Output;
            string src = Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "service.sh")));
            if (src == null) return "в архиве нет service.sh — похоже, перенос zapret изменился";
            string keepConf = File.Exists(Path.Combine(own, "conf.env")) ? File.ReadAllText(Path.Combine(own, "conf.env")) : null;
            if (Directory.Exists(own)) Directory.Delete(own, true);
            Directory.CreateDirectory(Path.GetDirectoryName(own)!);
            var mv = await Sys.Run("mv", new[] { src, own });
            if (mv.Code != 0) return "не получилось переложить zapret: " + mv.Output;
            if (keepConf != null) File.WriteAllText(Path.Combine(own, "conf.env"), keepConf);

            progress("скачиваю стратегии и nfqws…");
            // download-deps зовёт sudo только чтобы стереть свой же архив в /tmp. Из окна программы терминала нет,
            // и sudo там не спросит пароль, а упадёт — подкладываем «sudo», который просто выполняет команду от пользователя.
            // (не в /tmp: там бывает noexec)
            string shim = Path.Combine(Store.DataDir, ".nosudo");
            Directory.CreateDirectory(shim);
            string fakeSudo = Path.Combine(shim, "sudo");
            File.WriteAllText(fakeSudo, "#!/bin/sh\nwhile [ $# -gt 0 ] && [ \"${1#-}\" != \"$1\" ]; do shift; done\nexec \"$@\"\n");
            File.SetUnixFileMode(fakeSudo, (UnixFileMode)0b111_101_101);
            var env = new Dictionary<string, string> { ["PATH"] = shim + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin") };
            var dl = await Sys.Run("/usr/bin/env", new[] { "bash", Path.Combine(own, "service.sh"), "download-deps", "--default" }, 900000, own, env);
            Log.Write("zapret", "download-deps: код " + dl.Code + " — " + dl.Output.Replace('\n', ' '));
            if (!IsComplete(own)) return "не скачались стратегии или nfqws (код " + dl.Code + ") — подробности в журнале";
            Log.Write("zapret", "установлен в " + own + ", стратегий: " + Strategies().Count);
            return null;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
            try { Directory.Delete(Path.Combine(Store.DataDir, ".nosudo"), true); } catch { }
        }
    }
}
