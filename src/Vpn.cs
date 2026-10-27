// VPN-клиенты: Happ, INCY, Koala Clash — кто подключён, как подключить и выбрать сервер.
//   Happ  — ссылки happ://connect|disconnect, сервер — сохранённые «слоты» (список у Happ зашифрован);
//   INCY  — ссылки incy://connect|disconnect (официальная документация INCY-DEV/incy-docs);
//   Koala — API ядра mihomo через именованный канал \\.\pipe\Koala-Clash\mihomo: выбор сервера по номеру строки и TUN.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StartPack
{
    public class KoalaState
    {
        public bool Tun;
        public string Mode = "", Group = "GLOBAL", Now = "";
        public List<string> Servers = new List<string>();
        public int NowIndex { get { return Servers.IndexOf(Now) + 1; } }   // 0 — не из списка
    }

    // Снимок «кто подключён» — обновляется раз в 2 секунды.
    public class VpnSnapshot
    {
        public bool Happ, Incy, Warp;
        public KoalaState Koala;
        public bool KoalaRunning;
    }

    public static class VpnCtl
    {
        public static readonly string[] Kinds = { "warp", "happ", "incy", "koala" };

        public static bool IsVpn(Item it) { return Array.IndexOf(Kinds, it.Special) >= 0; }
        public static int Rank(Item it) { return Array.IndexOf(Kinds, it.Special); }

        public static string Title(string kind)
        {
            switch (kind) { case "warp": return "Cloudflare WARP"; case "happ": return "Happ"; case "incy": return "INCY"; case "koala": return "Koala Clash"; }
            return kind;
        }

        // У Happ и INCY ядро называется xray.exe — различаем по папке процесса.
        // Ядро Happ запускает системная служба, путь к нему недоступен — такой процесс считаем Happ'овским.
        public static void XrayOwners(HashSet<string> running, out bool happ, out bool incy)
        {
            happ = false; incy = false;
            bool happApp = running.Contains("Happ"), incyApp = running.Contains("incy");
            foreach (Process p in Process.GetProcessesByName("xray"))
            {
                string path = null;
                try { path = Core.ProcessPath(p.Id); } catch { }
                p.Dispose();
                if (path != null && path.IndexOf("incy", StringComparison.OrdinalIgnoreCase) >= 0) incy = true;
                else if (path != null && path.IndexOf("happ", StringComparison.OrdinalIgnoreCase) >= 0) happ = true;
                else if (path == null)
                {
                    if (happApp || !incyApp) happ = true; else incy = true;
                }
            }
        }

        public static bool IsConnected(Item it, VpnSnapshot s)
        {
            if (s == null) return false;
            switch (it.Special)
            {
                case "happ": return s.Happ;
                case "incy": return s.Incy;
                case "koala": return s.Koala != null && s.Koala.Tun;
                case "warp": return s.Warp;
            }
            return false;
        }

        // Расставить VPN в конец списка: Happ, INCY, Koala (остальное — в прежнем порядке).
        public static bool SortLast(Config c)
        {
            var other = c.Items.Where(i => !IsVpn(i)).ToList();
            var vpn = c.Items.Where(IsVpn).Select((it, n) => new { it, n }).OrderBy(x => Rank(x.it)).ThenBy(x => x.n).Select(x => x.it).ToList();
            var all = other.Concat(vpn).ToList();
            if (all.SequenceEqual(c.Items)) return false;
            c.Items = all;
            return true;
        }
    }

    // Cloudflare WARP: подключение — warp-cli (идёт вместе с WARP), состояние — по сетевому адаптеру CloudflareWARP.
    public static class WarpCtl
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cloudflare", "Cloudflare WARP"); } }
        public static string Exe { get { return Path.Combine(Dir, "Cloudflare WARP.exe"); } }
        public static string Cli { get { return Path.Combine(Dir, "warp-cli.exe"); } }

        public const string ServiceName = "CloudflareWARP";
        public static bool IsServiceError(string message) { return message != null && message.StartsWith("служба WARP не запущена"); }

        // Запустить службу WARP (Windows спросит права). true — запустилась.
        public static bool StartService()
        {
            var psi = new ProcessStartInfo("sc.exe", "start " + ServiceName) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using (Process p = Process.Start(psi)) p.WaitForExit(15000);
            for (int i = 0; i < 20; i++)
            {
                try { using (var sc = new System.ServiceProcess.ServiceController(ServiceName)) if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) return true; }
                catch { }
                System.Threading.Thread.Sleep(500);
            }
            return false;
        }

        public static bool Connected()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    bool warp = ni.Name.IndexOf("CloudflareWARP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ni.Description.IndexOf("Cloudflare WARP", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (warp && ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up) return true;
                }
            }
            catch { }
            return false;
        }

        // warp-cli connect / disconnect. Ошибка — понятным текстом.
        public static void Run(string cmd)
        {
            if (!File.Exists(Cli)) throw new Exception("не найден warp-cli — переустановите Cloudflare WARP");
            var psi = new ProcessStartInfo(Cli, "--accept-tos " + cmd)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                if (output.IndexOf("Unable to connect to the CloudflareWARP daemon", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("служба WARP не запущена — откройте Cloudflare WARP или перезагрузите ПК");
                if (output.IndexOf("Registration Missing", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("WARP ещё не настроен — откройте его один раз и примите условия");
                if (p.HasExited && p.ExitCode != 0) throw new Exception(output.Trim().Split('\n').FirstOrDefault() ?? "warp-cli вернул ошибку");
            }
        }
    }

    // INCY: ссылки incy://connect|disconnect (документация INCY-DEV/incy-docs).
    // У переносной версии Windows не знает, чем открывать incy:// — поэтому команда передаётся самому incy.exe:
    // второй запуск отдаёт её уже открытому INCY и сразу закрывается.
    public static class IncyCtl
    {
        static string ExeFor(Item it)
        {
            string p = it != null ? Core.Expand(it.Path) : "";
            if (p.EndsWith("incy.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p)) return p;
            foreach (Process pr in Process.GetProcessesByName("incy"))
            {
                string path = null;
                try { path = Core.ProcessPath(pr.Id); } catch { }
                pr.Dispose();
                if (path != null && File.Exists(path)) return path;
            }
            return StartPack.Detect.Incy();
        }

        public static void Send(Item it, string cmd)
        {
            string url = "incy://" + cmd;
            string exe = ExeFor(it);
            if (exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using (Process.Start(new ProcessStartInfo(exe, url) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })) { }
                return;
            }
            Core.ShellOpen(url);
        }

        // Файл настроек INCY: у переносной версии — рядом с программой, у установленной — в профиле.
        public static string PrefsFile(Item it)
        {
            string exe = ExeFor(it);
            var cands = new List<string>();
            if (exe != null) cands.Add(Path.Combine(Path.GetDirectoryName(exe), "config", "preferences.json"));
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
                foreach (string name in new[] { "incy", "INCY", ".incy" })
                {
                    cands.Add(Path.Combine(root, name, "preferences.json"));
                    cands.Add(Path.Combine(root, name, "config", "preferences.json"));
                }
            return cands.FirstOrDefault(File.Exists);
        }

        // Включить в самом INCY «подключаться при запуске» (только пока INCY закрыт, иначе он перезапишет файл).
        public static bool PresetAutoConnect(Item it, bool on) { return PresetFile(PrefsFile(it), on); }

        public static bool PresetFile(string f, bool on)
        {
            try
            {
                if (f == null) return false;
                string json = File.ReadAllText(f, Encoding.UTF8);
                var m = Regex.Match(json, "\"autoConnect\"\\s*:\\s*(true|false)");
                if (!m.Success || m.Groups[1].Value == (on ? "true" : "false")) return false;
                json = json.Substring(0, m.Groups[1].Index) + (on ? "true" : "false") + json.Substring(m.Groups[1].Index + m.Groups[1].Length);
                File.WriteAllText(f, json, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        static int SocksPort(Item it)
        {
            try
            {
                string f = PrefsFile(it);
                if (f == null) return 0;
                var m = Regex.Match(File.ReadAllText(f, Encoding.UTF8), "\"socksPort\"\\s*:\\s*(\\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
            catch { return 0; }
        }

        // Подключён ли INCY: его ядро xray (старые версии), свой туннель Wintun (режим TUN) или открытый им прокси-порт.
        public static bool Connected(Item it, HashSet<string> running, bool xrayIncy)
        {
            if (xrayIncy) return true;
            if (!running.Contains("incy")) return false;
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    string n = ni.Name + " " + ni.Description;
                    if (n.IndexOf("Wintun", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("incy", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // туннели других клиентов тоже бывают на Wintun — их не считаем
                    if (Regex.IsMatch(n, "happ|mihomo|meta|clash|koala|warp|wireguard|amnezia|sing-box|hiddify|nekobox|v2ray", RegexOptions.IgnoreCase)) continue;
                    return true;
                }
            }
            catch { }
            int port = SocksPort(it);
            return port > 0 && NetCheck.PortListenedBy(port, "incy");
        }
    }

    public static class KoalaCtl
    {
        const string PipeName = @"Koala-Clash\mihomo";
        static readonly HashSet<string> NotServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Direct", "Reject", "RejectDrop", "Pass", "Compatible", "Selector", "URLTest", "Fallback", "LoadBalance", "Relay" };

        public static string ConfigDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "koala-clash"); } }

        public static bool PipeExists()
        {
            try { return File.Exists(@"\\.\pipe\" + PipeName); } catch { return false; }
        }

        // ---------- HTTP поверх именованного канала (как у самой Koala)

        public static string Request(string method, string path, string body, int timeoutMs)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(timeoutMs);
                byte[] b = Encoding.UTF8.GetBytes(body ?? "");
                string head = method + " " + path + " HTTP/1.1\r\nHost: mihomo\r\nConnection: close\r\nAccept: application/json\r\n" +
                    (body != null ? "Content-Type: application/json\r\n" : "") + "Content-Length: " + b.Length + "\r\n\r\n";
                byte[] hb = Encoding.ASCII.GetBytes(head);
                pipe.Write(hb, 0, hb.Length);
                if (b.Length > 0) pipe.Write(b, 0, b.Length);
                pipe.Flush();

                var ms = new MemoryStream();
                var buf = new byte[65536];
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    Task<int> t = pipe.ReadAsync(buf, 0, buf.Length);
                    int left = Math.Max(200, timeoutMs - (int)sw.ElapsedMilliseconds);
                    if (!t.Wait(left)) throw new TimeoutException("Koala Clash не ответила");
                    int n = t.Result;
                    if (n == 0) break;
                    ms.Write(buf, 0, n);
                    if (Complete(ms.ToArray())) break;
                }
                int status;
                string text = Parse(ms.ToArray(), out status);
                if (status >= 400) throw new Exception("Koala Clash ответила ошибкой " + status + (text.Length > 0 ? ": " + text : ""));
                return text;
            }
        }

        static int HeaderEnd(byte[] d)
        {
            for (int i = 0; i + 3 < d.Length; i++)
                if (d[i] == 13 && d[i + 1] == 10 && d[i + 2] == 13 && d[i + 3] == 10) return i + 4;
            return -1;
        }

        static bool Complete(byte[] d)
        {
            int he = HeaderEnd(d);
            if (he < 0) return false;
            string head = Encoding.ASCII.GetString(d, 0, he);
            var m = Regex.Match(head, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return d.Length - he >= int.Parse(m.Groups[1].Value);
            if (Regex.IsMatch(head, @"Transfer-Encoding:\s*chunked", RegexOptions.IgnoreCase))
                return Encoding.ASCII.GetString(d, Math.Max(he, d.Length - 7), Math.Min(7, d.Length - he)).EndsWith("0\r\n\r\n");
            return head.StartsWith("HTTP/1.1 204") || head.StartsWith("HTTP/1.0 204");
        }

        static string Parse(byte[] d, out int status)
        {
            int he = HeaderEnd(d);
            if (he < 0) throw new IOException("Koala Clash прислала неполный ответ");
            string head = Encoding.ASCII.GetString(d, 0, he);
            var sm = Regex.Match(head, @"^HTTP/\d\.\d\s+(\d+)");
            status = sm.Success ? int.Parse(sm.Groups[1].Value) : 0;
            byte[] body = new byte[d.Length - he];
            Array.Copy(d, he, body, 0, body.Length);
            if (Regex.IsMatch(head, @"Transfer-Encoding:\s*chunked", RegexOptions.IgnoreCase))
            {
                var outp = new MemoryStream();
                int p = 0;
                while (p < body.Length)
                {
                    int le = p;
                    while (le + 1 < body.Length && !(body[le] == 13 && body[le + 1] == 10)) le++;
                    int size = Convert.ToInt32(Encoding.ASCII.GetString(body, p, le - p).Split(';')[0].Trim(), 16);
                    if (size == 0) break;
                    outp.Write(body, le + 2, size);
                    p = le + 2 + size + 2;
                }
                body = outp.ToArray();
            }
            return Encoding.UTF8.GetString(body);
        }

        // ---------- состояние, выбор сервера, TUN

        public static KoalaState GetState(int timeoutMs)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var st = new KoalaState();
            var cfg = js.Deserialize<Dictionary<string, object>>(Request("GET", "/configs", null, timeoutMs));
            object v;
            st.Mode = cfg.TryGetValue("mode", out v) ? Convert.ToString(v) : "";
            if (cfg.TryGetValue("tun", out v) && v is Dictionary<string, object>)
            {
                object en;
                if (((Dictionary<string, object>)v).TryGetValue("enable", out en)) st.Tun = Convert.ToBoolean(en);
            }
            var px = js.Deserialize<Dictionary<string, object>>(Request("GET", "/proxies", null, timeoutMs));
            var proxies = px.ContainsKey("proxies") ? px["proxies"] as Dictionary<string, object> : null;
            if (proxies == null) return st;
            Func<string, Dictionary<string, object>> get = name => proxies.ContainsKey(name) ? proxies[name] as Dictionary<string, object> : null;
            Func<Dictionary<string, object>, List<string>> all = g => g != null && g.ContainsKey("all") && g["all"] is IEnumerable
                ? ((IEnumerable)g["all"]).Cast<object>().Select(Convert.ToString).ToList() : new List<string>();
            Func<string, string> type = name => { var g = get(name); return g != null && g.ContainsKey("type") ? Convert.ToString(g["type"]) : ""; };

            Dictionary<string, object> global = get("GLOBAL");
            string group = "GLOBAL";
            if (!st.Mode.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                // в режиме «правила» сервер выбирается в первой группе-переключателе
                string sel = all(global).FirstOrDefault(n => type(n).Equals("Selector", StringComparison.OrdinalIgnoreCase));
                if (sel != null) group = sel;
            }
            Dictionary<string, object> g0 = get(group);
            st.Group = group;
            st.Now = g0 != null && g0.ContainsKey("now") ? Convert.ToString(g0["now"]) : "";
            st.Servers = all(g0).Where(n => !NotServers.Contains(type(n))).ToList();
            return st;
        }

        public static void Select(string group, string server, int timeoutMs)
        {
            string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "name", server } });
            Request("PUT", "/proxies/" + Uri.EscapeDataString(group), body, timeoutMs);
        }

        public static void SetTun(bool on, int timeoutMs)
        {
            Request("PATCH", "/configs", "{\"tun\":{\"enable\":" + (on ? "true" : "false") + "}}", timeoutMs);
        }

        // Koala ещё не запущена: отметить «TUN включён» в её настройках — при старте она подключится сама
        // (так же хранит состояние её собственный главный переключатель).
        public static bool PresetTun(bool on)
        {
            try
            {
                string f = Path.Combine(ConfigDir, "mihomo.yaml");
                if (!File.Exists(f)) return false;
                string s = File.ReadAllText(f, Encoding.UTF8);
                var re = new Regex(@"(?m)^(tun:\r?\n[ \t]+enable:[ \t]*)(true|false)");
                if (!re.IsMatch(s)) return false;
                string n = re.Replace(s, "${1}" + (on ? "true" : "false"), 1);
                if (n != s) File.WriteAllText(f, n, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static bool AppRunning(HashSet<string> running) { return running.Contains("Koala Clash"); }
    }
}
