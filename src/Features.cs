// Свой автозапуск программ (как в Диспетчере задач), проверка связи, управление zapret, уборка.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace StartPack
{
    // ---------------------------------------------------------------- собственный автозапуск программ

    // Включение/выключение записи из HKCU\...\Run тем же способом, что «Диспетчер задач → Автозагрузка»:
    // сама запись не удаляется (программа не вернёт её назад), а помечается выключенной в StartupApproved.
    public static class AutoStartCtl
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // Имя записи автозапуска, которая запускает эту программу (или null).
        public static string FindEntry(Item it)
        {
            var targets = new List<string>();
            string p = Core.Expand(it.Path);
            if (p.Length > 0 && !Core.IsShell(p))
            {
                targets.Add(p);
                if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string t = Core.ResolveLnk(p);
                    if (t.Length > 0) targets.Add(t);
                }
            }
            if (targets.Count == 0) return null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return null;
                    foreach (string name in k.GetValueNames())
                    {
                        string cmd = Environment.ExpandEnvironmentVariables(Convert.ToString(k.GetValue(name)) ?? "");
                        if (targets.Any(t => cmd.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) return name;
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool IsEnabled(string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    var v = k == null ? null : k.GetValue(name) as byte[];
                    return v == null || v.Length == 0 || (v[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        public static void SetEnabled(string name, bool on)
        {
            var data = new byte[12];
            data[0] = (byte)(on ? 2 : 3);
            if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                k.SetValue(name, data, RegistryValueKind.Binary);
            Journal.Write("автозапуск", "запись «" + name + "»: " + (on ? "включена" : "выключена"));
        }
    }

    // ---------------------------------------------------------------- проверка связи

    public class NetTarget
    {
        public string Name, Url, Host;
        public int Port;
        public bool ViaProxy;       // Telegram: пинг — до сервера, к которому подключён TG WS Proxy
        public string State = "";   // "", "checking", "ok", "fail"
        public int Ms;
        public string Error;
    }

    public static class NetCheck
    {
        // Какие сервисы проверять — по тому, что есть в списке.
        public static List<NetTarget> TargetsFor(Config cfg)
        {
            var list = new List<NetTarget>();
            bool zapret = cfg.Items.Any(i => i.UpdateKind == "zapret");
            bool discord = zapret || cfg.Items.Any(i => i.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase));
            if (zapret) list.Add(new NetTarget { Name = "YouTube", Url = "https://www.youtube.com/generate_204" });
            if (discord) list.Add(new NetTarget { Name = "Discord", Url = "https://discord.com/api/v9/gateway" });
            if (cfg.Items.Any(i => i.Special == "spotify")) list.Add(new NetTarget { Name = "Spotify", Url = "https://apresolve.spotify.com/?type=accesspoint" });
            if (cfg.Items.Any(i => i.Repo.Equals("Flowseal/tg-ws-proxy", StringComparison.OrdinalIgnoreCase)))
                list.Add(new NetTarget { Name = "Telegram", Host = "127.0.0.1", Port = TgWsPort(), ViaProxy = true });
            return list;
        }

        // Порт, на котором слушает TG WS Proxy (из его настроек).
        public static int TgWsPort()
        {
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TgWsProxy", "config.json");
                if (File.Exists(cfg))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(cfg));
                    object v;
                    if (d.TryGetValue("port", out v)) return Convert.ToInt32(v);
                }
            }
            catch { }
            return 1443;
        }

        public static Task Check(NetTarget t)
        {
            t.State = "checking";
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                bool noPing = false;   // прокси работает, но пока ни к чему не подключён — пинг не меряем
                try
                {
                    if (t.Host != null)
                    {
                        // сначала — отвечает ли прокси на этом ПК (TG WS Proxy)
                        using (var c = new TcpClient())
                        {
                            Task conn = c.ConnectAsync(t.Host, t.Port);
                            if (!conn.Wait(3000) || !c.Connected) throw new Exception("TG WS Proxy не отвечает на " + t.Host + ":" + t.Port + " — он запущен?");
                        }
                        sw.Restart();
                        if (t.ViaProxy)
                        {
                            IPEndPoint up = ProxyUpstream(t.Port);
                            noPing = up == null;
                            if (up != null)
                            {
                                using (var c = new TcpClient(up.AddressFamily))
                                {
                                    Task conn = c.ConnectAsync(up.Address, up.Port);
                                    if (!conn.Wait(4000) || !c.Connected) throw new Exception("TG WS Proxy работает, но его сервер не отвечает (" + up + ")");
                                }
                            }
                        }
                    }
                    if (t.Url != null)
                    {
                        var req = (HttpWebRequest)WebRequest.Create(t.Url);
                        req.Method = "GET";
                        req.AllowAutoRedirect = false;
                        req.Timeout = 8000;
                        req.ReadWriteTimeout = 8000;
                        req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StartPack/" + Core.Version;
                        try { using (req.GetResponse()) { } }
                        catch (WebException ex)
                        {
                            // Пришёл любой HTTP-ответ (403, 404…) — значит, сайт доступен.
                            if (ex.Response == null) throw;
                            ex.Response.Dispose();
                        }
                    }
                    t.Ms = noPing ? -1 : (int)sw.ElapsedMilliseconds;
                    t.State = "ok";
                    t.Error = null;
                }
                catch (Exception ex)
                {
                    Exception e = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    t.State = "fail";
                    t.Error = e is WebException ? Describe((WebException)e) : e.Message;
                }
            });
        }

        // Куда сейчас подключён процесс, слушающий порт прокси (TG WS Proxy держит несколько соединений наготове).
        public static IPEndPoint ProxyUpstream(int port)
        {
            var rows = TcpRows();
            uint pid = 0;
            foreach (var r in rows) if (r.State == 2 /* LISTEN */ && r.LocalPort == port) { pid = r.Pid; break; }
            if (pid == 0) return null;
            foreach (var r in rows)
                if (r.Pid == pid && r.State == 5 /* ESTABLISHED */ && !IPAddress.IsLoopback(r.Remote.Address)) return r.Remote;
            return null;
        }

        class TcpRow { public uint State, Pid; public int LocalPort; public IPEndPoint Remote; }

        // Слушает ли этот порт процесс с таким именем (например, прокси-порт INCY).
        public static bool PortListenedBy(int port, string processName)
        {
            foreach (var r in TcpRows())
            {
                if (r.State != 2 || r.LocalPort != port) continue;
                try { using (Process p = Process.GetProcessById((int)r.Pid)) return p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        static List<TcpRow> TcpRows()
        {
            var list = new List<TcpRow>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /* AF_INET */, 5 /* TCP_TABLE_OWNER_PID_ALL */, 0);
            IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return list;
                int n = System.Runtime.InteropServices.Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr row = buf + 4 + i * 24;
                    Func<int, uint> u = off => (uint)System.Runtime.InteropServices.Marshal.ReadInt32(row, off);
                    Func<uint, int> port = p => (int)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF));
                    list.Add(new TcpRow
                    {
                        State = u(0), LocalPort = port(u(8)), Pid = u(20),
                        Remote = new IPEndPoint(new IPAddress((long)u(12)), port(u(16)))
                    });
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            return list;
        }

        static string Describe(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout: return "нет ответа (тайм-аут)";
                case WebExceptionStatus.NameResolutionFailure: return "не найден адрес (DNS)";
                case WebExceptionStatus.ConnectFailure: return "соединение не установлено";
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.TrustFailure: return "оборвано шифрованное соединение — похоже на блокировку";
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.ReceiveFailure: return "соединение оборвалось";
                default: return ex.Message;
            }
        }
    }

    // ---------------------------------------------------------------- zapret: стратегии, остановка, подбор

    // Замер одной стратегии zapret: из теста стратегий (utils\test results) или из проверки связи Старт-пака.
    public class StrategyStat
    {
        public string Bat { get; set; }
        public string When { get; set; }        // yyyy-MM-dd HH:mm
        public string Source { get; set; }      // "test" — тест стратегий, "check" — проверка связи
        public int YtMs { get; set; }           // -1 — нет ответа или не мерили
        public int DcMs { get; set; }
        public bool YtOk { get; set; }
        public bool DcOk { get; set; }
        public int Score { get; set; }          // 0…100: доля успешных проверок
        public StrategyStat() { Bat = ""; When = ""; Source = ""; YtMs = -1; DcMs = -1; }
    }

    public static class StrategyQuality
    {
        // "best", "good", "bad", "none" или "" — не проверялась
        public static string Of(Item it, string bat)
        {
            StrategyStat s = it.Strategies.FirstOrDefault(x => x.Bat.Equals(bat, StringComparison.OrdinalIgnoreCase));
            if (s == null) return "";
            if (!s.YtOk && !s.DcOk) return "none";
            if (!s.YtOk || !s.DcOk || s.Score < 70 || Math.Max(s.YtMs, s.DcMs) > 1500) return "bad";
            // «наилучшая» — самая надёжная и быстрая среди хороших (если сравнивать есть с чем)
            var good = it.Strategies.Where(x => x.YtOk && x.DcOk && x.Score >= 70 && Math.Max(x.YtMs, x.DcMs) <= 1500).ToList();
            if (good.Count >= 2)
            {
                StrategyStat best = good.OrderByDescending(x => x.Score).ThenBy(x => Avg(x)).First();
                if (best == s) return "best";
            }
            return "good";
        }

        static int Avg(StrategyStat s)
        {
            int a = s.YtMs >= 0 ? s.YtMs : 3000, b = s.DcMs >= 0 ? s.DcMs : 3000;
            return (a + b) / 2;
        }

        public static string Label(string q)
        {
            switch (q) { case "best": return "Наилучшее"; case "good": return "Хорошее"; case "bad": return "Плохое"; case "none": return "Нет связи"; }
            return "не проверялась";
        }

        public static string Tip(Item it, string bat)
        {
            StrategyStat s = it.Strategies.FirstOrDefault(x => x.Bat.Equals(bat, StringComparison.OrdinalIgnoreCase));
            if (s == null) return "Ещё не проверялась. Включите её и нажмите ↻ у связи — или «Подобрать тестом», чтобы проверить все сразу.";
            Func<bool, int, string> v = (ok, ms) => !ok ? "нет связи" : ms >= 0 ? ms + " мс" : "есть связь";
            string when = s.When;
            DateTime d;
            if (DateTime.TryParse(s.When, out d))
                when = d.Date == DateTime.Today ? "сегодня в " + d.ToString("HH:mm") : d.Date == DateTime.Today.AddDays(-1) ? "вчера в " + d.ToString("HH:mm") : d.ToString("dd.MM в HH:mm");
            return "YouTube: " + v(s.YtOk, s.YtMs) + "\nDiscord: " + v(s.DcOk, s.DcMs) +
                (s.Source == "test" ? "\nУспешных проверок в тесте: " + s.Score + "%" : "") +
                "\nЗамер: " + when + (s.Source == "test" ? ", тест стратегий" : ", проверка связи");
        }

        public static void Put(Item it, StrategyStat s)
        {
            it.Strategies.RemoveAll(x => x.Bat.Equals(s.Bat, StringComparison.OrdinalIgnoreCase));
            it.Strategies.Add(s);
        }

        // Самый свежий файл результатов теста zapret — если ещё не учтён. true — что-то добавилось.
        public static bool ImportTest(Item it)
        {
            try
            {
                string dir = Path.Combine(ZapretCtl.Dir(it) ?? "", "utils", "test results");
                if (!Directory.Exists(dir)) return false;
                FileInfo f = new DirectoryInfo(dir).GetFiles("test_results_*.txt").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (f == null || f.Name == it.TestImported) return false;
                var stats = ParseTest(File.ReadAllLines(f.FullName), f.LastWriteTime);
                foreach (StrategyStat s in stats)
                {
                    // свежий замер проверки связи не затираем более старым тестом
                    StrategyStat old = it.Strategies.FirstOrDefault(x => x.Bat.Equals(s.Bat, StringComparison.OrdinalIgnoreCase));
                    DateTime a, b;
                    if (old != null && DateTime.TryParse(old.When, out a) && DateTime.TryParse(s.When, out b) && a > b) continue;
                    Put(it, s);
                }
                it.TestImported = f.Name;
                return stats.Count > 0;
            }
            catch { return false; }
        }

        // Блоки «Config: general (ALT).bat (Type: standard)» со строками «  YouTube Web : HTTP:OK TLS1.3:OK | Ping: 51 ms».
        public static List<StrategyStat> ParseTest(IEnumerable<string> lines, DateTime when)
        {
            var list = new List<StrategyStat>();
            StrategyStat cur = null;
            int ok = 0, all = 0;
            Action close = () =>
            {
                if (cur == null) return;
                cur.Score = all > 0 ? ok * 100 / all : 0;
                list.Add(cur);
            };
            bool ytAny = false, dcAny = false;
            foreach (string raw in lines)
            {
                var m = Regex.Match(raw, @"^Config:\s*(.+?\.bat)\s*\(Type:\s*standard\)");
                if (m.Success)
                {
                    if (cur != null) { if (!ytAny) cur.YtOk = false; if (!dcAny) cur.DcOk = false; }
                    close();
                    cur = new StrategyStat { Bat = m.Groups[1].Value.Trim(), When = when.ToString("yyyy-MM-dd HH:mm"), Source = "test", YtOk = true, DcOk = true };
                    ok = 0; all = 0; ytAny = false; dcAny = false;
                    continue;
                }
                if (raw.StartsWith("Config:") || raw.StartsWith("===")) { if (cur != null) { if (!ytAny) cur.YtOk = false; if (!dcAny) cur.DcOk = false; } close(); cur = null; continue; }
                if (cur == null) continue;
                var t = Regex.Match(raw, @"^\s+(.+?)\s*:\s*(.*?)\s*\|\s*Ping:\s*(.+)$");
                if (!t.Success) continue;
                string name = t.Groups[1].Value, http = t.Groups[2].Value, ping = t.Groups[3].Value;
                int good = Regex.Matches(http, "OK").Count, bad = Regex.Matches(http, "ERROR|SSL|UNSUP").Count;
                ok += good; all += good + bad;
                var pm = Regex.Match(ping, @"(\d+)\s*ms");
                int ms = pm.Success ? int.Parse(pm.Groups[1].Value) : -1;
                bool works = good > 0 && good >= bad;
                if (name.StartsWith("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    ytAny = true;
                    if (!works) cur.YtOk = false;
                    if (name.IndexOf("Web", StringComparison.OrdinalIgnoreCase) >= 0 || cur.YtMs < 0) cur.YtMs = ms;
                }
                else if (name.StartsWith("Discord", StringComparison.OrdinalIgnoreCase))
                {
                    dcAny = true;
                    if (!works) cur.DcOk = false;
                    if (name.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0 || cur.DcMs < 0) cur.DcMs = ms;
                }
            }
            if (cur != null) { if (!ytAny) cur.YtOk = false; if (!dcAny) cur.DcOk = false; }
            close();
            return list;
        }
    }

    public static class ZapretCtl
    {
        public static string Dir(Item it) { return Core.SafeDir(Core.Expand(it.Path)); }

        // Все стратегии — файлы general*.bat в папке zapret (порядок как в самом zapret).
        public static List<string> Strategies(Item it)
        {
            string d = Dir(it);
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) return new List<string>();
            return Directory.GetFiles(d, "general*.bat").Select(f => Path.GetFileName(f))
                .OrderBy(n => Regex.Replace(n, @"\d+", m => m.Value.PadLeft(8, '0')), StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Pretty(string bat)
        {
            string n = Path.GetFileNameWithoutExtension(bat ?? "");
            var m = Regex.Match(n, @"^general\s*\((.+)\)$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : n;
        }

        public static string Current(Item it) { return Core.SafeFileName(Core.Expand(it.Path)); }

        // Остановить zapret (winws работает с правами администратора — Windows спросит разрешение).
        public static void Stop()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c taskkill /F /IM winws.exe")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process.Start(psi)) { }
        }

        // --- службы Windows, которые zapret оставляет после себя
        // Драйвер WinDivert (и служба zapret, если её ставили через service.bat) запоминают папку, из которой
        // их впервые запустили. Пока драйвер загружен из старой папки, её нельзя удалить, а новый zapret
        // продолжает работать со старым драйвером.

        public class Svc { public string Name, Bin, Root; }

        static List<Svc> svcCache;
        static DateTime svcTime;

        public static List<Svc> Services()
        {
            if (svcCache != null && (DateTime.UtcNow - svcTime).TotalSeconds < 3) return svcCache;
            var list = new List<Svc>();
            foreach (string name in new[] { "zapret", "WinDivert", "WinDivert14" })
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                    {
                        if (k == null) continue;
                        string bin = BinOf(k.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                        if (bin == null) continue;
                        string dir = Core.SafeDir(bin);
                        string root = dir != null && Path.GetFileName(dir).Equals("bin", StringComparison.OrdinalIgnoreCase) ? Core.SafeDir(dir) : null;
                        // только папки zapret: драйвер других программ (например, GoodbyeDPI) не трогаем
                        if (root == null || !File.Exists(Path.Combine(root, "service.bat"))) continue;
                        list.Add(new Svc { Name = name, Bin = bin, Root = root });
                    }
                }
                catch { }
            }
            svcCache = list;
            svcTime = DateTime.UtcNow;
            return list;
        }

        public static void ForgetServices() { svcCache = null; }

        static string BinOf(string img)
        {
            img = (img ?? "").Trim();
            if (img.Length == 0) return null;
            string p;
            if (img.StartsWith("\""))
            {
                int q = img.IndexOf('"', 1);
                p = q > 0 ? img.Substring(1, q - 1) : img.Trim('"');
            }
            else
            {
                var m = Regex.Match(img, @"^(.+?\.(exe|sys))(\s|$)", RegexOptions.IgnoreCase);
                p = m.Success ? m.Groups[1].Value : img;
            }
            if (p.StartsWith(@"\??\")) p = p.Substring(4);
            p = Environment.ExpandEnvironmentVariables(p);
            return p.Length > 3 && p[1] == ':' ? p : null;
        }

        public static bool SameDir(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { a = Path.GetFullPath(a); b = Path.GetFullPath(b); } catch { }
            return a.TrimEnd('\\').Equals(b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // Службы zapret, оставшиеся от другой папки (не той, что выбрана в списке).
        public static List<Svc> Foreign(string currentDir)
        {
            return Services().Where(s => !SameDir(s.Root, currentDir)).ToList();
        }

        public static List<Svc> Inside(IEnumerable<string> dirs)
        {
            var d = dirs.ToList();
            return Services().Where(s => d.Any(x => SameDir(s.Root, x))).ToList();
        }

        // Команда cmd: остановить и удалить службы (драйвер WinDivert создастся заново из новой папки при запуске zapret).
        public static string ResetCmd(IEnumerable<Svc> svcs)
        {
            var sb = new StringBuilder();
            foreach (Svc s in svcs.OrderBy(s => s.Name == "zapret" ? 0 : 1))
                sb.Append("sc stop ").Append(s.Name).Append(" >nul 2>&1 & sc delete ").Append(s.Name).Append(" >nul 2>&1 & ");
            return sb.ToString();
        }

        public static string Describe(IEnumerable<Svc> svcs)
        {
            return string.Join(", ", svcs.Select(s => (s.Name == "zapret" ? "служба zapret" : "драйвер " + s.Name) + " из «" + Path.GetFileName(s.Root) + "»"));
        }

        // Снять службы с правами администратора (Windows спросит разрешение) и, если нужно, запустить zapret заново.
        public static Process ResetServices(IEnumerable<Svc> svcs, string restartBat)
        {
            string cmd = "taskkill /F /IM winws.exe >nul 2>&1 & " + ResetCmd(svcs) + "timeout /t 1 /nobreak >nul";
            if (!string.IsNullOrEmpty(restartBat)) cmd += " & call \"" + restartBat + "\"";
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = string.IsNullOrEmpty(restartBat) ? Path.GetTempPath() : Path.GetDirectoryName(restartBat)
            };
            ForgetServices();
            Journal.Write("zapret", "снимаю " + Describe(svcs) + (restartBat != null ? " и перезапускаю" : ""));
            return Process.Start(psi);
        }

        public static string TestScript(Item it)
        {
            string d = Dir(it);
            return d == null ? null : Path.Combine(d, "utils", "test zapret.ps1");
        }

        // Встроенный тест zapret: открывается в своём окне (там нужно выбрать вариант), Windows спросит права.
        public static Process StartTest(Item it)
        {
            string ps1 = TestScript(it);
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"")
            {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(ps1)
            };
            return Process.Start(psi);
        }

        // Лучшая стратегия по самому свежему файлу результатов теста (после указанного времени).
        public static string BestFromResults(Item it, DateTime since, out string summary)
        {
            summary = null;
            try
            {
                string dir = Path.Combine(Dir(it), "utils", "test results");
                if (!Directory.Exists(dir)) return null;
                FileInfo f = new DirectoryInfo(dir).GetFiles("test_results_*.txt").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (f == null || f.LastWriteTime < since) return null;
                string best = null;
                int bestOk = -1, bestPing = -1;
                bool inAnalytics = false;
                foreach (string line in File.ReadAllLines(f.FullName))
                {
                    if (line.Contains("=== ANALYTICS ===")) { inAnalytics = true; continue; }
                    if (!inAnalytics) continue;
                    var m = Regex.Match(line, @"^(.+?\.bat)\s*:\s*HTTP OK:\s*(\d+).*?Ping OK:\s*(\d+)");
                    if (!m.Success) continue;
                    int ok = int.Parse(m.Groups[2].Value), ping = int.Parse(m.Groups[3].Value);
                    if (ok > bestOk || (ok == bestOk && ping > bestPing)) { best = m.Groups[1].Value.Trim(); bestOk = ok; bestPing = ping; }
                }
                if (best != null) summary = Pretty(best) + ": прошло проверок " + bestOk + ", пинг ок " + bestPing;
                return best;
            }
            catch { return null; }
        }
    }

    // ---------------------------------------------------------------- уборка

    public class CleanupTask
    {
        public string Title, Hint, Kind;   // Kind: "move-tgws", "recycle", "zapret-old"
        public Item Item;
        public List<string> Paths = new List<string>();
        public List<ZapretCtl.Svc> Services = new List<ZapretCtl.Svc>();
        public bool Selected = true;
    }

    public static class Cleanup
    {
        public static List<CleanupTask> Scan(Config cfg)
        {
            var list = new List<CleanupTask>();
            string apps = Templates.AppsDir;
            foreach (Item it in cfg.Items)
            {
                string p = Core.Expand(it.Path);
                if (p.Length == 0 || Core.IsShell(p)) continue;
                string dir = Core.SafeDir(p);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                if (it.Repo.Equals("Flowseal/tg-ws-proxy", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                {
                    if (!dir.StartsWith(apps, StringComparison.OrdinalIgnoreCase))
                        list.Add(new CleanupTask
                        {
                            Kind = "move-tgws", Item = it, Paths = { p },
                            Title = "Перенести TG WS Proxy в папку Старт-пака",
                            Hint = "Сейчас он лежит в «" + Path.GetFileName(dir) + "» — его легко удалить случайно. Его настройки не пострадают."
                        });
                    var dups = Directory.GetFiles(dir, "TgWsProxy*.exe").Where(f => !f.Equals(p, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (dups.Count > 0)
                        list.Add(new CleanupTask
                        {
                            Kind = "recycle", Item = it, Paths = dups,
                            Title = "Убрать в корзину лишние копии TG WS Proxy",
                            Hint = string.Join(", ", dups.Select(d => Path.GetFileName(d) + VersionOf(d)))
                        });
                }

                if (it.UpdateKind == "zapret")
                {
                    // старые копии zapret в любых папках (не новее той, что выбрана)
                    string cur = Core.ZapretVersion(dir);
                    var olds = Detect.ZapretAll()
                        .Where(z => !ZapretCtl.SameDir(z.Root, dir) && Core.CompareVersions(z.Version, cur) <= 0)
                        .Select(z => z.Root).ToList();
                    if (olds.Count > 0)
                    {
                        var svcs = ZapretCtl.Inside(olds);
                        string parent = Core.SafeDir(dir);
                        list.Add(new CleanupTask
                        {
                            Kind = "zapret-old", Item = it, Paths = olds, Services = svcs,
                            Title = "Убрать в корзину старые версии zapret",
                            Hint = string.Join(", ", olds.Select(d => ZapretCtl.SameDir(Core.SafeDir(d), parent) ? Path.GetFileName(d) : d))
                                + " — сейчас используется " + Path.GetFileName(dir) + " (" + cur + ")"
                                + (svcs.Count > 0 ? ".\nИз старой папки ещё работает " + ZapretCtl.Describe(svcs)
                                    + " — Старт-пак снимет его (Windows спросит права) и перезапустит zapret из новой папки." : "")
                        });
                    }
                }

                // остатки от обновлений
                var leftovers = new List<string>();
                foreach (string ext in new[] { ".old", ".download", ".download.part", ".download.part.etag" })
                    if (File.Exists(p + ext)) leftovers.Add(p + ext);
                if (leftovers.Count > 0)
                    list.Add(new CleanupTask { Kind = "recycle", Item = it, Paths = leftovers, Title = "Убрать остатки обновления «" + it.Name + "»", Hint = string.Join(", ", leftovers.Select(Path.GetFileName)) });
            }
            return list;
        }

        static string VersionOf(string exe)
        {
            try
            {
                string v = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
                return string.IsNullOrEmpty(v) ? "" : " (" + Regex.Replace(v, @"(\.0)+$", "") + ")";
            }
            catch { return ""; }
        }

        // В корзину без системных окон: при ошибке — исключение с понятной причиной (кто держит файлы).
        public static void Recycle(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return;
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path.TrimEnd('\\') + "\0\0",
                fFlags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_WANTNUKEWARNING
            };
            int rc = SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted || Directory.Exists(path) || File.Exists(path))
            {
                string who = Holders(path);
                throw new IOException(who != null ? "занято: " + who
                    : "Windows не дала убрать" + (rc != 0 ? " (код " + rc + ")" : "") + " — возможно, папка открыта в Проводнике или её сейчас синхронизирует OneDrive");
            }
            Journal.Write("уборка", "в корзину: " + path);
        }

        // Кто держит файлы папки: программы (Restart Manager) и службы zapret, запущенные из неё.
        public static string Holders(string path)
        {
            var names = new List<string>();
            try
            {
                foreach (ZapretCtl.Svc s in ZapretCtl.Inside(new[] { path }))
                    names.Add(s.Name == "zapret" ? "служба zapret" : "драйвер " + s.Name + " (остался от zapret)");
            }
            catch { }
            try
            {
                string[] files = Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(800).ToArray()
                    : new[] { path };
                foreach (string n in RmHolders(files)) if (!names.Contains(n)) names.Add(n);
            }
            catch { }
            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        static List<string> RmHolders(string[] files)
        {
            var res = new List<string>();
            if (files.Length == 0) return res;
            uint h;
            if (RmStartSession(out h, 0, new StringBuilder(64)) != 0) return res;
            try
            {
                if (RmRegisterResources(h, (uint)files.Length, files, 0, IntPtr.Zero, 0, null) != 0) return res;
                uint need = 0, n = 0, reasons = 0;
                int rc = RmGetList(h, out need, ref n, null, ref reasons);
                if (rc == 234 && need > 0)   // ERROR_MORE_DATA
                {
                    var info = new RM_PROCESS_INFO[need];
                    n = need;
                    if (RmGetList(h, out need, ref n, info, ref reasons) == 0)
                        for (int i = 0; i < n; i++)
                        {
                            string name = info[i].strAppName;
                            try { using (Process p = Process.GetProcessById(info[i].Process.dwProcessId)) name = p.ProcessName + ".exe"; } catch { }
                            if (!string.IsNullOrEmpty(name) && !res.Contains(name)) res.Add(name);
                        }
                }
            }
            finally { RmEndSession(h); }
            return res;
        }

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool bRestartable;
        }

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);
    }
}
