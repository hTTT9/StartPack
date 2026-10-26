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
        public bool Happ, Incy;
        public KoalaState Koala;
        public bool KoalaRunning;
    }

    public static class VpnCtl
    {
        public static readonly string[] Kinds = { "happ", "incy", "koala" };

        public static bool IsVpn(Item it) { return Array.IndexOf(Kinds, it.Special) >= 0; }
        public static int Rank(Item it) { return Array.IndexOf(Kinds, it.Special); }

        public static string Title(string kind)
        {
            switch (kind) { case "happ": return "Happ"; case "incy": return "INCY"; case "koala": return "Koala Clash"; }
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

    public static class IncyCtl
    {
        public static void Send(string cmd) { Core.ShellOpen("incy://" + cmd); }
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
