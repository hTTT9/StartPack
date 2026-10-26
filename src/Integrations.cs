// Связка с Happ (подключение, сохранённые серверы) и со SpotifyHostsFix (проверка и починка hosts).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace StartPack
{
    // Happ понимает ссылки happ://connect, happ://disconnect, happ://close (проверено на Happ 4.3).
    // Подключён ли он — видно по процессу xray, который поднимает служба Happ.
    // Последний выбранный сервер Happ хранит в реестре (lastServer*) и читает при запуске.
    public static class HappCtl
    {
        const string Key = @"Software\Happ\OrganizationDefaults\Preferences";

        public static bool IsRunning(HashSet<string> running) { return running.Contains("Happ"); }
        public static bool IsConnected(HashSet<string> running) { return running.Contains("xray"); }

        public static bool IsRunningNow() { return Core.RunningNames().Contains("Happ"); }
        public static bool IsConnectedNow() { return Core.RunningNames().Contains("xray"); }

        public static string CurrentServerName()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                    return k == null ? null : k.GetValue("lastServerName") as string;
            }
            catch { return null; }
        }

        public static string CurrentGuid()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                    return k == null ? null : k.GetValue("lastServerGuid") as string;
            }
            catch { return null; }
        }

        public static bool IsCurrent(HappPreset p)
        {
            return string.Equals(CurrentGuid() ?? "", p.Guid, StringComparison.OrdinalIgnoreCase);
        }

        // Запомнить сервер, который сейчас выбран в Happ.
        public static HappPreset Capture()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                if (k == null) return null;
                var p = new HappPreset { Name = (k.GetValue("lastServerName") as string) ?? "Сервер" };
                foreach (string n in k.GetValueNames())
                {
                    if (!n.StartsWith("lastServer", StringComparison.Ordinal) && n != "lastSubscription") continue;
                    RegistryValueKind kind = k.GetValueKind(n);
                    object v = k.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    p.Values.Add(new RegVal { Name = n, Kind = kind.ToString(), Value = Ser(v, kind) });
                }
                return p.Guid.Length == 0 ? null : p;
            }
        }

        // Подставить сохранённый сервер. Делать только когда Happ закрыт — при выходе он пишет свои значения.
        public static void Apply(HappPreset p)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                foreach (RegVal rv in p.Values)
                {
                    var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), rv.Kind);
                    k.SetValue(rv.Name, Deser(rv.Value, kind), kind);
                }
        }

        static string Ser(object v, RegistryValueKind kind)
        {
            if (v == null) return "";
            switch (kind)
            {
                case RegistryValueKind.DWord: return Convert.ToInt32(v).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])v);
                case RegistryValueKind.MultiString: return string.Join("\n", (string[])v);
                default: return Convert.ToString(v);
            }
        }

        static object Deser(string s, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return int.Parse(s, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return long.Parse(s, CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary: return Convert.FromBase64String(s);
                case RegistryValueKind.MultiString: return s.Split('\n');
                default: return s;
            }
        }

        // Для самопроверки: значения пресета после записи-чтения совпадут с тем, что сейчас в реестре (без записи).
        public static bool RoundTripOk(HappPreset p, out string details)
        {
            var bad = new List<string>();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                foreach (RegVal rv in p.Values)
                {
                    var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), rv.Kind);
                    object back = Deser(rv.Value, kind);
                    object now = k.GetValue(rv.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (k.GetValueKind(rv.Name) != kind || Ser(back, kind) != Ser(now, kind)) bad.Add(rv.Name);
                }
            details = string.Join(", ", p.Values.Select(v => v.Name + ":" + v.Kind)) + (bad.Count > 0 ? " | расхождения: " + string.Join(", ", bad) : "");
            return bad.Count == 0;
        }

        public static void Send(string cmd) { Core.ShellOpen("happ://" + cmd); }

        public static async Task<bool> WaitFor(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                await Task.Delay(300);
            }
            return cond();
        }

        // Подключить уже открытый Happ; если не вышло — повторить один раз.
        public static async Task<bool> Connect()
        {
            if (IsConnectedNow()) return true;
            Send("connect");
            if (await WaitFor(IsConnectedNow, 15000)) return true;
            Send("connect");
            return await WaitFor(IsConnectedNow, 12000);
        }

        public static async Task<bool> Disconnect()
        {
            if (!IsConnectedNow()) return true;
            Send("disconnect");
            return await WaitFor(() => !IsConnectedNow(), 10000);
        }

        public static async Task<bool> Close()
        {
            if (!IsRunningNow()) return true;
            Send("close");
            return await WaitFor(() => !IsRunningNow(), 12000);
        }
    }

    // SpotifyHostsFix: Spotify-Hosts-Update.ps1 сам ищет живой прокси-IP и пишет его в hosts.
    public static class HostsFix
    {
        public const string Script = "Spotify-Hosts-Update.ps1";
        public const string TaskName = "Spotify Hosts Auto-Update";
        const string BlockBegin = "# >>> spotify-hosts-auto BEGIN";
        const string BlockEnd = "# <<< spotify-hosts-auto END";

        public static string Find()
        {
            foreach (string root in Detect.Roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    if (File.Exists(Path.Combine(root, Script))) return root;
                    foreach (string d in Directory.GetDirectories(root))
                        if (File.Exists(Path.Combine(d, Script))) return d;
                }
                catch { }
            }
            return null;
        }

        public static bool Valid(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try { return File.Exists(Path.Combine(Core.Expand(dir), Script)); } catch { return false; }
        }

        static string HostsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts"); } }

        // Адреса, которые сейчас прописаны в hosts для Spotify.
        public static List<string> CurrentIps()
        {
            var ips = new List<string>();
            try
            {
                bool inBlock = false, hasBlock = false;
                string[] lines = File.ReadAllLines(HostsPath);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.StartsWith(BlockBegin)) { inBlock = true; hasBlock = true; continue; }
                    if (line.StartsWith(BlockEnd)) { inBlock = false; continue; }
                    if (!inBlock) continue;
                    var m = Regex.Match(line, @"^(\d{1,3}(?:\.\d{1,3}){3})\s+\S");
                    if (m.Success && !ips.Contains(m.Groups[1].Value)) ips.Add(m.Groups[1].Value);
                }
                if (!hasBlock)
                    foreach (string raw in lines)
                    {
                        var m = Regex.Match(raw.Trim(), @"^(\d{1,3}(?:\.\d{1,3}){3})\s+\S*(spotify|scdn)", RegexOptions.IgnoreCase);
                        if (m.Success && !ips.Contains(m.Groups[1].Value)) ips.Add(m.Groups[1].Value);
                    }
            }
            catch { }
            return ips;
        }

        public class ProbeResult
        {
            public bool Ran, CurrentOk, AnyOk;
            public string CurrentIp, Error;
        }

        // Проверка без прав администратора (-ProbeOnly): hosts не меняется.
        public static Task<ProbeResult> Probe(string dir)
        {
            return Task.Run(() =>
            {
                var r = new ProbeResult();
                List<string> current = CurrentIps();
                r.CurrentIp = current.FirstOrDefault() ?? "";
                try
                {
                    string ps1 = Path.Combine(Core.Expand(dir), Script);
                    var psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + ps1 + "\" -ProbeOnly -NoPause")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                        WorkingDirectory = Core.Expand(dir)
                    };
                    using (Process p = Process.Start(psi))
                    {
                        Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                        p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } r.Error = "проверка зависла"; return r; }
                        string text = outTask.Result;
                        r.Ran = true;
                        r.AnyOk = p.ExitCode == 0;
                        foreach (string line in text.Split('\n'))
                        {
                            var m = Regex.Match(line, @"^\s+(\d{1,3}(?:\.\d{1,3}){3})\s+(\d+)/(\d+)");
                            if (!m.Success || !current.Contains(m.Groups[1].Value)) continue;
                            int score = int.Parse(m.Groups[2].Value), total = int.Parse(m.Groups[3].Value);
                            if (total > 0 && score == total) { r.CurrentOk = true; r.CurrentIp = m.Groups[1].Value; }
                        }
                    }
                }
                catch (Exception ex) { r.Error = ex.Message; }
                return r;
            });
        }

        public class FixResult
        {
            public int ExitCode;
            public string Message;
        }

        // Запускает скрипт с правами администратора (Windows спросит разрешение) и ждёт результата.
        // Process.Start нужно звать из окна (STA-поток), ожидание — в фоне.
        public static async Task<FixResult> RunElevated(string dir, string args)
        {
            string d = Core.Expand(dir);
            string ps1 = Path.Combine(d, Script);
            DateTime start = DateTime.Now.AddSeconds(-2);
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\" -NoPause " + args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = d
            };
            Process p = Process.Start(psi);   // отказ в UAC — Win32Exception 1223
            var r = new FixResult { ExitCode = -1 };
            if (p != null)
            {
                using (p)
                {
                    await Task.Run(() => p.WaitForExit(180000));
                    try { if (p.HasExited) r.ExitCode = p.ExitCode; } catch { }
                }
            }
            r.Message = LastLogMessage(d, start);
            return r;
        }

        static string LastLogMessage(string dir, DateTime since)
        {
            try
            {
                string log = Path.Combine(dir, "logs", "update.log");
                if (!File.Exists(log)) return null;
                string best = null;
                foreach (string line in File.ReadAllLines(log, Encoding.UTF8).Reverse().Take(200))
                {
                    var m = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\s+(.*)$");
                    if (!m.Success) continue;
                    DateTime t;
                    if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    if (t < since) break;
                    string msg = m.Groups[2].Value.Trim();
                    if (msg.Length == 0 || msg.StartsWith("Время работы") || msg.StartsWith("причина")) continue;
                    if (Regex.IsMatch(msg, "Готово|работает|не изменён|НЕ изменён|Автообновление|удален|удалён|Задача|Задачи")) return msg;
                    if (best == null && !Regex.IsMatch(msg, @"^\d{1,3}(\.\d{1,3}){3}")) best = msg;
                }
                return best;
            }
            catch { return null; }
        }

        public static bool TaskInstalled()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/query /tn \"" + TaskName + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}
