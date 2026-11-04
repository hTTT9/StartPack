// Работа с системой Linux: процессы (/proc), запуск и остановка программ, права администратора (pkexec),
// дистрибутив (/etc/os-release), flatpak, автозапуск (~/.config/autostart).

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StartPack.Linux;

public record ProcInfo(int Pid, string Comm, string Exe, string CmdLine);

public static class Sys
{
    // ---------------------------------------------------------------- процессы

    // Все процессы: имя (comm, до 15 символов), argv[0] и вся командная строка.
    public static List<ProcInfo> Processes()
    {
        var list = new List<ProcInfo>();
        if (!Directory.Exists("/proc")) return list;
        foreach (string d in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(d), out int pid)) continue;
            try
            {
                string comm = File.ReadAllText(Path.Combine(d, "comm")).Trim();
                string cmd = File.ReadAllText(Path.Combine(d, "cmdline"));
                string[] argv = cmd.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                list.Add(new ProcInfo(pid, comm, argv.Length > 0 ? argv[0] : "", string.Join(" ", argv)));
            }
            catch { /* процесс завершился или нет доступа */ }
        }
        return list;
    }

    static IEnumerable<string> Names(string spec) =>
        (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Совпадает ли процесс с именем: comm (обрезан до 15 символов) или имя файла из argv[0].
    public static bool Matches(ProcInfo p, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string comm15 = name.Length > 15 ? name[..15] : name;
        if (p.Comm.Equals(comm15, StringComparison.Ordinal)) return true;
        string exe = Path.GetFileName(p.Exe);
        return exe.Equals(name, StringComparison.Ordinal);
    }

    public static List<ProcInfo> Find(string processSpec, List<ProcInfo> all = null)
    {
        all ??= Processes();
        var names = Names(processSpec).ToList();
        return all.Where(p => p.Pid != Environment.ProcessId && names.Any(n => Matches(p, n))).ToList();
    }

    public static bool IsRunning(string processSpec, List<ProcInfo> all = null) => Find(processSpec, all).Count > 0;

    [DllImport("libc", SetLastError = true)] static extern int kill(int pid, int sig);
    [DllImport("libc")] static extern uint geteuid();

    public static bool IsRoot => OperatingSystem.IsLinux() && geteuid() == 0;

    // Мягко закрыть (SIGTERM), через 5 секунд — принудительно. Чужие (root) процессы так не закрыть — false.
    public static async Task<bool> Stop(string processSpec)
    {
        var procs = Find(processSpec);
        if (procs.Count == 0) return true;
        foreach (var p in procs) kill(p.Pid, 15);
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(200);
            if (Find(processSpec).Count == 0) return true;
        }
        foreach (var p in Find(processSpec)) kill(p.Pid, 9);
        await Task.Delay(500);
        return Find(processSpec).Count == 0;
    }

    // ---------------------------------------------------------------- запуск

    public static string Which(string cmd)
    {
        if (cmd.Contains('/')) return File.Exists(cmd) ? cmd : null;
        string path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (string dir in path.Split(':').Concat(new[] { "/usr/sbin", "/sbin", "/snap/bin", Path.Combine(Store.Home, ".local", "bin") }))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string f = Path.Combine(dir, cmd);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    // Запустить и не ждать: программа живёт своей жизнью и после закрытия Старт-пака.
    public static void Launch(string file, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = Store.Home };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi);
    }

    public static IEnumerable<string> SplitArgs(string s)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return res;
        var cur = new System.Text.StringBuilder();
        bool q = false;
        foreach (char c in s)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ' ' && !q) { if (cur.Length > 0) { res.Add(cur.ToString()); cur.Clear(); } continue; }
            cur.Append(c);
        }
        if (cur.Length > 0) res.Add(cur.ToString());
        return res;
    }

    public record RunResult(int Code, string Output);

    // Выполнить и дождаться (с ограничением по времени), вывод — вместе.
    public static async Task<RunResult> Run(string file, IEnumerable<string> args, int timeoutMs = 120000, string workDir = null, IDictionary<string, string> env = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            WorkingDirectory = workDir ?? Store.Home
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        using var p = System.Diagnostics.Process.Start(psi);
        // Пустой ввод: если скрипт вдруг спросит что-то через read, он получит «конец ввода», а не зависнет.
        try { p.StandardInput.Close(); } catch { }
        var sb = new System.Text.StringBuilder();
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        var done = p.WaitForExitAsync();
        if (await Task.WhenAny(done, Task.Delay(timeoutMs)) != done)
        {
            try { p.Kill(true); } catch { }
            return new RunResult(-1, "не закончилось за " + timeoutMs / 1000 + " с");
        }
        sb.Append(await o).Append(await e);
        return new RunResult(p.ExitCode, sb.ToString());
    }

    // ---------------------------------------------------------------- права администратора

    // pkexec показывает системное окно с паролем. В самопроверке (CI) — sudo -n без пароля.
    public static string[] ElevatePrefix()
    {
        if (IsRoot) return Array.Empty<string>();
        string forced = Environment.GetEnvironmentVariable("STARTPACK_ELEVATE");
        if (forced == "sudo") return new[] { "sudo", "-n" };
        if (Which("pkexec") != null) return new[] { "pkexec" };
        if (Which("sudo") != null) return new[] { "sudo", "-n" };
        return null;
    }

    public static async Task<RunResult> RunElevated(string file, IEnumerable<string> args, int timeoutMs = 600000, string workDir = null)
    {
        string[] pre = ElevatePrefix();
        if (pre == null) return new RunResult(-2, "в системе нет pkexec и sudo — нечем получить права администратора");
        if (pre.Length == 0) return await Run(file, args, timeoutMs, workDir);
        var all = pre.Skip(1).Append(file).Concat(args).ToList();
        RunResult r = await Run(pre[0], all, timeoutMs, workDir);
        // pkexec: 126 — пользователь отменил окно, 127 — не прошла проверка подлинности
        if (pre[0] == "pkexec" && (r.Code == 126 || r.Code == 127))
            return new RunResult(r.Code, "отменено — без прав администратора это не сделать");
        return r;
    }

    // ---------------------------------------------------------------- дистрибутив

    public record OsInfo(string Id, string IdLike, string Name, string Version)
    {
        public bool IsDebianLike => Id is "debian" or "ubuntu" or "linuxmint" or "pop" || IdLike.Contains("debian") || IdLike.Contains("ubuntu");
        public bool IsFedoraLike => Id is "fedora" or "redos" or "rhel" or "centos" or "almalinux" or "rocky" || IdLike.Contains("fedora") || IdLike.Contains("rhel");
        public string PackageManager => IsDebianLike ? "apt" : IsFedoraLike ? "dnf" : null;
    }

    public static OsInfo Os(string file = "/etc/os-release")
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (string line in File.ReadAllLines(file))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                d[line[..i]] = line[(i + 1)..].Trim().Trim('"');
            }
        }
        catch { }
        string Get(string k) => d.TryGetValue(k, out string v) ? v : "";
        return new OsInfo(Get("ID").ToLowerInvariant(), Get("ID_LIKE").ToLowerInvariant(), Get("PRETTY_NAME"), Get("VERSION_ID"));
    }

    // ---------------------------------------------------------------- flatpak

    public static bool FlatpakInstalled(string appId) =>
        Directory.Exists(Path.Combine(Store.Home, ".local", "share", "flatpak", "app", appId)) ||
        Directory.Exists(Path.Combine("/var/lib/flatpak/app", appId));

    public static bool HasFlatpak => Which("flatpak") != null;

    // ---------------------------------------------------------------- открыть ссылку / файл

    public static void Open(string what)
    {
        try { Launch(Which("xdg-open") ?? "xdg-open", new[] { what }); }
        catch (Exception ex) { Log.Write("открыть", what + ": " + ex.Message); }
    }

    // ---------------------------------------------------------------- автозапуск при входе

    public static string AutostartFile =>
        Path.Combine(Path.GetDirectoryName(Store.ConfigDir)!, "autostart", "startpack.desktop");

    // Путь к самой программе: у AppImage — сам файл .AppImage, иначе — исполняемый файл.
    public static string SelfPath => Environment.GetEnvironmentVariable("APPIMAGE") is string a && a.Length > 0 ? a : Environment.ProcessPath;

    public static bool AutostartEnabled => File.Exists(AutostartFile);

    public static void SetAutostart(bool on)
    {
        if (!on) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile,
            "[Desktop Entry]\nType=Application\nName=Старт-пак\nComment=Запустить всё при входе в систему\n" +
            "Exec=\"" + SelfPath + "\" --autorun\nIcon=startpack\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
    }
}
