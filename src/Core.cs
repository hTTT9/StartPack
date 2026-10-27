// Старт-пак: данные, список программ, запуск, версии, обновления с GitHub.
// Язык: C# 5 (так умеет встроенный в Windows csc) — без $"", ?. и т. п.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Старт-пак")]
[assembly: System.Reflection.AssemblyProduct("Старт-пак")]
[assembly: System.Reflection.AssemblyVersion("3.9.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.9.0.0")]

namespace StartPack
{
    // ---------------------------------------------------------------- данные

    public class RegVal
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Value { get; set; }
    }

    // Сервер Happ, запомненный из его настроек (значения lastServer* из реестра).
    public class HappPreset
    {
        public string Name { get; set; }
        public List<RegVal> Values { get; set; }

        public HappPreset() { Name = ""; Values = new List<RegVal>(); }

        [ScriptIgnore]
        public string Guid
        {
            get
            {
                RegVal v = Values.FirstOrDefault(x => x.Name == "lastServerGuid");
                return v == null ? "" : (v.Value ?? "");
            }
        }

        public HappPreset Copy()
        {
            return new HappPreset
            {
                Name = Name,
                Values = Values.Select(v => new RegVal { Name = v.Name, Kind = v.Kind, Value = v.Value }).ToList()
            };
        }
    }

    public class Item
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Args { get; set; }
        public string ProcessName { get; set; }
        public int Delay { get; set; }
        public bool Admin { get; set; }
        public bool SkipIfRunning { get; set; }
        public bool Enabled { get; set; }
        public string UpdateKind { get; set; }   // "", "zapret", "github-exe"
        public string Repo { get; set; }
        public string Asset { get; set; }
        public string InstalledTag { get; set; }
        public long InstalledSize { get; set; }

        public string Special { get; set; }      // "", "happ", "spotify"
        public bool HappAutoConnect { get; set; }
        public string HappServer { get; set; }   // имя сохранённого сервера; "" — последний выбранный в Happ
        public List<HappPreset> HappPresets { get; set; }
        public string HostsFixDir { get; set; }
        public bool SpotifyCheckHosts { get; set; }

        public string Id { get; set; }            // постоянный номер — для профилей
        public bool Watch { get; set; }           // сторож: следить, что программа не закрылась
        public bool WaitReady { get; set; }       // в «Запустить всё» ждать, пока процесс появится
        public int VpnServer { get; set; }        // Koala: номер строки сервера (0 — как выбрано в самой Koala)

        // Состояние во время работы — в файл не пишется.
        [ScriptIgnore] public string Message { get; set; }
        [ScriptIgnore] public bool MessageIsError { get; set; }
        [ScriptIgnore] public string LocalVersion { get; set; }
        [ScriptIgnore] public string LatestTag { get; set; }
        [ScriptIgnore] public string LatestUrl { get; set; }
        [ScriptIgnore] public string CheckError { get; set; }
        [ScriptIgnore] public bool Busy { get; set; }
        [ScriptIgnore] public int Progress { get; set; }
        [ScriptIgnore] public string HostsState { get; set; }   // "", "checking", "ok", "dead", "fixing", "error"
        [ScriptIgnore] public string HostsIp { get; set; }

        public Item()
        {
            Name = ""; Path = ""; Args = ""; ProcessName = ""; UpdateKind = ""; Repo = ""; Asset = ""; InstalledTag = "";
            Special = ""; HappServer = ""; HostsFixDir = ""; HappPresets = new List<HappPreset>();
            SkipIfRunning = true; Enabled = true; Progress = -1; HostsState = ""; HostsIp = "";
            Id = System.Guid.NewGuid().ToString("N").Substring(0, 12); WaitReady = true;
        }

        [ScriptIgnore]
        public bool UpdateAvailable
        {
            get
            {
                return !string.IsNullOrEmpty(LatestTag) && !string.IsNullOrEmpty(LatestUrl)
                    && Core.CompareVersions(LatestTag, LocalVersion) > 0;
            }
        }

        public HappPreset FindPreset()
        {
            if (string.IsNullOrEmpty(HappServer)) return null;
            return HappPresets.FirstOrDefault(p => p.Name == HappServer);
        }

        public void Normalize()
        {
            Name = Name ?? ""; Path = Path ?? ""; Args = Args ?? ""; ProcessName = ProcessName ?? "";
            UpdateKind = UpdateKind ?? ""; Repo = Repo ?? ""; Asset = Asset ?? ""; InstalledTag = InstalledTag ?? "";
            Special = Special ?? ""; HappServer = HappServer ?? ""; HostsFixDir = HostsFixDir ?? "";
            if (HappPresets == null) HappPresets = new List<HappPreset>();
            HappPresets.RemoveAll(p => p == null);
            foreach (HappPreset p in HappPresets) { p.Name = p.Name ?? ""; if (p.Values == null) p.Values = new List<RegVal>(); }
            if (Delay < 0) Delay = 0;
            if (string.IsNullOrEmpty(Id)) Id = System.Guid.NewGuid().ToString("N").Substring(0, 12);
            Progress = -1; HostsState = ""; HostsIp = "";
        }

        public Item Clone()
        {
            Item c = (Item)MemberwiseClone();
            c.HappPresets = HappPresets.Select(p => p.Copy()).ToList();
            return c;
        }
    }

    // Профиль — какие карточки включены в «Запустить всё».
    public class Profile
    {
        public string Name { get; set; }
        public List<string> Ids { get; set; }
        public Profile() { Name = ""; Ids = new List<string>(); }
    }

    public class Config
    {
        public int Version { get; set; }
        public string Theme { get; set; }        // "dark", "light", "system"
        public bool CloseAfterLaunch { get; set; }
        public bool CheckUpdatesOnStart { get; set; }
        public bool Tray { get; set; }           // жить в трее: сторож, горячая клавиша
        public bool Hotkey { get; set; }         // Ctrl+Alt+S — запустить всё
        public bool NetCheck { get; set; }       // проверять связь после запуска
        public bool Overlay { get; set; }        // маленький оверлей поверх окон
        public int OverlayOpacity { get; set; }  // 20…100 %
        public bool OverlayClickThrough { get; set; }
        public int OverlayX { get; set; }
        public int OverlayY { get; set; }
        public string OverlayShow { get; set; }  // какие программы: "enabled" (из «Запустить всё»), "all", "running"
        public bool OverlayStatus { get; set; }  // текст состояния справа
        public bool OverlayNet { get; set; }     // строка связи с сервисами
        public bool OverlayMs { get; set; }      // пинг в миллисекундах
        public bool OverlayButtons { get; set; } // кнопки ▶/■ и ↻
        public int OverlayScale { get; set; }    // размер: 85, 100, 120 %
        public bool TipAutostartShown { get; set; }   // подсказка «Запуск с Windows» уже показана
        public int AutorunDelay { get; set; }    // секунд ожидания перед «Запустить всё» при входе в Windows
        public bool UpdateNotify { get; set; }   // напоминать об обновлениях из трея
        public string ActiveProfile { get; set; }
        public List<Profile> Profiles { get; set; }
        public int WinX { get; set; }
        public int WinY { get; set; }
        public int WinW { get; set; }
        public int WinH { get; set; }
        public List<Item> Items { get; set; }
        public List<DonateAddr> Donate { get; set; }   // «Поблагодарить автора»: криптоадреса
        public string DonateUrl { get; set; }          // и страница DonationAlerts; всё пустое — пункт скрыт

        // Version = 0: в файле версии 1 этого поля нет — так видно, что список нужно перенести.
        public Config()
        {
            Version = 0; Theme = "dark"; CloseAfterLaunch = true; CheckUpdatesOnStart = true;
            Tray = true; Hotkey = true; NetCheck = true; ActiveProfile = ""; Profiles = new List<Profile>();
            OverlayOpacity = 85; OverlayX = -1; OverlayY = -1; UpdateNotify = false;
            OverlayShow = "enabled"; OverlayStatus = true; OverlayNet = true; OverlayMs = true; OverlayButtons = true; OverlayScale = 100;
            Items = new List<Item>();
            Donate = DonateAddr.Defaults();
            DonateUrl = DonateAddr.DefaultUrl;
        }

        [ScriptIgnore]
        public Profile Active
        {
            get
            {
                Profile p = Profiles.FirstOrDefault(x => x.Name == ActiveProfile);
                if (p == null && Profiles.Count > 0) { p = Profiles[0]; ActiveProfile = p.Name; }
                return p;
            }
        }

        // Записать включённые карточки в текущий профиль.
        public void StoreProfile()
        {
            Profile p = Active;
            if (p == null) return;
            p.Ids = Items.Where(i => i.Enabled).Select(i => i.Id).ToList();
        }

        public void LoadProfile(string name)
        {
            Profile p = Profiles.FirstOrDefault(x => x.Name == name);
            if (p == null) return;
            ActiveProfile = p.Name;
            foreach (Item i in Items) i.Enabled = p.Ids.Contains(i.Id);
        }

        public void EnsureProfiles()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            Profiles.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            foreach (Profile p in Profiles) if (p.Ids == null) p.Ids = new List<string>();
            if (Profiles.Count == 0)
            {
                Profiles.Add(new Profile { Name = "Всё", Ids = Items.Where(i => i.Enabled).Select(i => i.Id).ToList() });
                var bypass = Items.Where(i => i.UpdateKind.Length > 0 || i.Special == "happ").Select(i => i.Id).ToList();
                if (bypass.Count > 0 && bypass.Count < Items.Count) Profiles.Add(new Profile { Name = "Только обход блокировок", Ids = bypass });
                ActiveProfile = "Всё";
            }
            if (Active != null) LoadProfile(Active.Name);
        }
    }

    public class Release
    {
        public string Tag;
        public List<KeyValuePair<string, string>> Assets = new List<KeyValuePair<string, string>>();
    }

    // ---------------------------------------------------------------- логика без окон

    public static class Core
    {
        public const string Title = "Старт-пак";
        public const string IpsetNone = "203.0.113.113/32";
        public const int ConfigVersion = 6;
        public const string Version = "3.9";

        public static bool FirstRun;

        public static bool HasDonate(Config c)
        {
            return (c.Donate != null && c.Donate.Any(d => d != null && !string.IsNullOrWhiteSpace(d.Address))) || !string.IsNullOrWhiteSpace(c.DonateUrl);
        }

        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string ConfigPath { get { return Path.Combine(AppDir, "StartPack.json"); } }

        // --- список

        public static Config Load(out string warning)
        {
            warning = null;
            if (!File.Exists(ConfigPath))
            {
                Config fresh = Detect.Defaults();
                FirstRun = true;
                Save(fresh);
                return fresh;
            }
            try
            {
                Config c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (c == null) c = new Config();
                if (c.Items == null) c.Items = new List<Item>();
                c.Items.RemoveAll(i => i == null);
                foreach (Item i in c.Items) i.Normalize();
                if (string.IsNullOrEmpty(c.Theme)) c.Theme = "dark";
                if (c.Version < ConfigVersion)
                {
                    Migrate(c);
                    c.EnsureProfiles();
                    Save(c);
                }
                c.EnsureProfiles();
                // список мог прийти с другого ПК или программы переустановлены — найти их заново
                if (Repair(c).Count > 0) Save(c);
                return c;
            }
            catch (Exception ex)
            {
                try { File.Copy(ConfigPath, ConfigPath + ".broken", true); } catch { }
                warning = "Не удалось прочитать список программ (StartPack.json): " + ex.Message +
                    "\n\nСтарый файл сохранён как StartPack.json.broken, список собран заново.";
                Config fresh = Detect.Defaults();
                Save(fresh);
                return fresh;
            }
        }

        // Импорт: файл настроек, сохранённый «Экспортом» (с этого или другого ПК). Пути чинятся под этот ПК.
        public static Config FromFile(string file)
        {
            string json = File.ReadAllText(file, Encoding.UTF8);
            Config c;
            try { c = new JavaScriptSerializer().Deserialize<Config>(json); }
            catch (Exception ex) { throw new Exception("Это не файл настроек Старт-пака (" + ex.Message + ")."); }
            if (c == null || c.Items == null || !json.Contains("\"Items\""))
                throw new Exception("Это не файл настроек Старт-пака: в нём нет списка программ.");
            c.Items.RemoveAll(i => i == null);
            foreach (Item i in c.Items) i.Normalize();
            if (string.IsNullOrEmpty(c.Theme)) c.Theme = "dark";
            if (string.IsNullOrEmpty(c.OverlayShow)) c.OverlayShow = "enabled";
            if (c.OverlayScale <= 0) c.OverlayScale = 100;
            if (c.Version < ConfigVersion) Migrate(c);
            c.EnsureProfiles();
            Repair(c);
            return c;
        }

        public static void ExportTo(Config c, string file)
        {
            File.WriteAllText(file, Pretty(new JavaScriptSerializer().Serialize(c)), new UTF8Encoding(false));
            Journal.Write("настройки", "экспорт в " + file);
        }

        // Список из версии 1: отметить Happ и Spotify и добавить Spotify, если он есть на ПК.
        static void Migrate(Config c)
        {
            if (c.Version >= 2 && c.Version < 6 && !c.Items.Any(i => i.Special == "warp"))
            {
                // версия 6: Cloudflare WARP в стандартном наборе — над VPN, выключенный
                string found = Installers.SafeDetect(Installers.Get("warp"));
                Item w = Templates.Make("warp", found);
                w.Enabled = false;
                c.Items.Add(w);
                VpnCtl.SortLast(c);
                Journal.Write("список", "добавлен Cloudflare WARP (выключен)" + (found == null ? ", не установлен" : ""));
            }
            if (c.Version < 2)
            {
                foreach (Item it in c.Items)
                {
                    string p = Expand(it.Path);
                    string file = SafeFileName(p);
                    if (it.Special.Length == 0 && (file.Equals("Happ.lnk", StringComparison.OrdinalIgnoreCase) || file.Equals("Happ.exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        it.Special = "happ";
                        it.HappAutoConnect = true;
                    }
                    if (it.Special.Length == 0 && file.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        it.Special = "spotify";
                        it.SpotifyCheckHosts = true;
                        it.HostsFixDir = HostsFix.Find() ?? "";
                    }
                }
                if (!c.Items.Any(i => i.Special == "spotify"))
                {
                    Item sp = Templates.Spotify();
                    if (sp != null)
                    {
                        // перед Telegram/Discord, после прокси
                        int at = c.Items.FindIndex(i => i.ProcessName == "Telegram" || i.ProcessName == "Discord");
                        if (at < 0) c.Items.Add(sp); else c.Items.Insert(at, sp);
                    }
                }
            }
            if (c.Version < 5)
            {
                // версия 5: установленные VPN-клиенты, которых нет в списке, — добавить выключенными
                // (включать два VPN разом в «Запустить всё» нельзя — пусть человек выберет сам)
                foreach (Func<Item> make in new Func<Item>[] { Templates.Happ, Templates.Incy, Templates.Koala })
                {
                    Item v;
                    try { v = make(); } catch { v = null; }
                    if (v == null || c.Items.Any(i => i.Special == v.Special)) continue;
                    v.Enabled = false;
                    c.Items.Add(v);
                    Journal.Write("список", "добавлен найденный VPN «" + v.Name + "» (выключен)");
                }
                VpnCtl.SortLast(c);
            }
            if (c.Version < 4)
            {
                // версия 4: VPN — в конец списка (Happ, INCY, Koala)
                VpnCtl.SortLast(c);
            }
            if (c.Version < 3)
            {
                // версия 3: сторож для zapret и TG WS Proxy, ожидание готовности, профили
                foreach (Item it in c.Items)
                {
                    if (it.UpdateKind.Length > 0) it.Watch = true;
                    it.WaitReady = true;
                }
            }
            c.Version = ConfigVersion;
        }

        public static bool Frozen;   // Старт-пак удаляется — ничего не записывать

        public static void Save(Config c)
        {
            if (Frozen) return;
            string json = Pretty(new JavaScriptSerializer().Serialize(c));
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Copy(tmp, ConfigPath, true);
            File.Delete(tmp);
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (inStr)
                {
                    sb.Append(ch);
                    if (ch == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (ch == '"') inStr = false;
                    continue;
                }
                switch (ch)
                {
                    case '"': inStr = true; sb.Append(ch); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(ch).Append(json[++i]); break; }
                        sb.Append(ch); ind++; sb.Append("\r\n").Append(' ', ind * 2); break;
                    case '}':
                    case ']': ind--; sb.Append("\r\n").Append(' ', ind * 2).Append(ch); break;
                    case ',': sb.Append(ch).Append("\r\n").Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        // --- пути

        public static string Expand(string p)
        {
            return Environment.ExpandEnvironmentVariables((p ?? "").Trim().Trim('"'));
        }

        public static bool IsShell(string p) { return p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase); }

        public static bool PathExists(string raw)
        {
            string p = Expand(raw);
            if (p.Length == 0) return false;
            if (IsShell(p)) return ShellAppInstalled(p);
            try { return File.Exists(p) || Directory.Exists(p); } catch { return false; }
        }

        // --- починка путей: список с другого компьютера или после переустановки программ

        public static List<string> LastRepairs = new List<string>();

        // Возвращает названия карточек, которым нашёлся новый путь.
        public static List<string> Repair(Config c)
        {
            var fixes = new List<string>();
            foreach (Item it in c.Items)
            {
                string old = it.Path;
                if (!PathExists(it.Path))
                {
                    string found = RemapUser(Expand(it.Path));
                    if (found == null)
                    {
                        string kind = Installers.KindOf(it);
                        AppSource a = kind == null ? null : Installers.Get(kind);
                        string det = a == null ? null : Installers.SafeDetect(a);
                        if (det != null)
                        {
                            found = det;
                            if (kind == "zapret")
                            {
                                // сохранить выбранную стратегию, если такой .bat есть в найденной папке
                                string same = Path.Combine(SafeDir(det) ?? "", SafeFileName(Expand(old)));
                                if (File.Exists(same)) found = same;
                            }
                            if (kind == "tgws") it.ProcessName = Path.GetFileNameWithoutExtension(det);
                            if (kind == "discord" && it.Args.Length == 0) it.Args = "--processStart Discord.exe";
                        }
                    }
                    if (found == null) found = FromRunningProcess(it, Expand(old));
                    if (found != null && !string.Equals(found, old, StringComparison.OrdinalIgnoreCase))
                    {
                        // заготовка «не установлена» из стандартного набора: программу поставили — включить её
                        if (!VpnCtl.IsVpn(it) && Expand(old).Length == 0) it.Enabled = true;
                        it.Path = found;
                        it.InstalledTag = "";
                        it.InstalledSize = 0;
                        it.LocalVersion = null;
                        fixes.Add(it.Name);
                        Journal.Write("поиск", it.Name + ": " + old + " → " + found);
                    }
                }
                if (it.Special == "spotify" && !HostsFix.Valid(it.HostsFixDir))
                {
                    string dir = it.HostsFixDir.Length > 0 ? RemapUser(Expand(it.HostsFixDir)) : null;
                    if (dir == null || !HostsFix.Valid(dir)) dir = HostsFix.Find();
                    if (dir != null && dir != it.HostsFixDir)
                    {
                        Journal.Write("поиск", it.Name + ": папка SpotifyHostsFix → " + dir);
                        it.HostsFixDir = dir;
                        if (!fixes.Contains(it.Name)) fixes.Add(it.Name);
                    }
                }
            }
            LastRepairs = fixes;
            return fixes;
        }

        // Если программа сейчас запущена — взять путь у её процесса (работает и для запущенных от администратора).
        public static string FromRunningProcess(Item it, string oldPath)
        {
            string kind = Installers.KindOf(it);
            foreach (string name in ProcNames(it))
                foreach (Process pr in Process.GetProcessesByName(name))
                {
                    string exe = null;
                    try { exe = ProcessPath(pr.Id); } catch { }
                    pr.Dispose();
                    if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                    string dir = SafeDir(exe) ?? "";
                    if (kind == "zapret")
                    {
                        // winws.exe лежит в <папка zapret>\bin
                        string root = SafeDir(dir);
                        if (root == null) continue;
                        foreach (string bat in new[] { SafeFileName(oldPath), "general (ALT).bat", "general.bat" })
                            if (bat.Length > 0 && File.Exists(Path.Combine(root, bat))) return Path.Combine(root, bat);
                        continue;
                    }
                    if (kind == "discord")
                    {
                        // ...\Discord\app-1.0.xxxx\Discord.exe → ...\Discord\Update.exe
                        string up = Path.Combine(SafeDir(dir) ?? "", "Update.exe");
                        if (SafeFileName(dir).StartsWith("app-", StringComparison.OrdinalIgnoreCase) && File.Exists(up)) return up;
                    }
                    if (oldPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || oldPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsShell(oldPath))
                        return exe;
                }
            return null;
        }

        // C:\Users\другой\... → C:\Users\текущий\... (и рабочий стол — с OneDrive или без).
        public static string RemapUser(string p)
        {
            if (string.IsNullOrEmpty(p) || IsShell(p)) return null;
            var m = Regex.Match(p, @"^[A-Za-z]:\\Users\\[^\\]+(\\.*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string rest = m.Groups[1].Value;
            var cands = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + rest };
            var md = Regex.Match(rest, @"^\\(OneDrive[^\\]*\\)?(Desktop|Рабочий стол)(\\.*)?$", RegexOptions.IgnoreCase);
            if (md.Success) cands.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + md.Groups[3].Value);
            foreach (string cnd in cands)
            {
                try { if (File.Exists(cnd) || Directory.Exists(cnd)) return cnd; } catch { }
            }
            return null;
        }

        // --- установлено ли приложение из «Пуска»/Store (список читается один раз и кэшируется)

        static HashSet<string> shellApps;
        static readonly object shellLock = new object();

        public static bool ShellAppInstalled(string p)
        {
            const string prefix = "shell:AppsFolder\\";
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;   // прочие shell:-пути не проверяем
            string id = p.Substring(prefix.Length);
            lock (shellLock)
            {
                if (shellApps == null)
                {
                    shellApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in StartApps()) shellApps.Add(a.Value);
                    if (shellApps.Count == 0) return true;   // не удалось прочитать список — не пугать «не найден»
                }
                return shellApps.Contains(id);
            }
        }

        public static void ResetShellCache() { lock (shellLock) shellApps = null; }

        public static string SafeDir(string p)
        {
            try { return Path.GetDirectoryName(p); } catch { return null; }
        }

        public static string SafeFileName(string p)
        {
            try { return Path.GetFileName(p) ?? ""; } catch { return ""; }
        }

        public static string ResolveLnk(string lnk)
        {
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = sh.CreateShortcut(lnk);
                return (string)sc.TargetPath ?? "";
            }
            catch { return ""; }
        }

        public static void CreateShortcut(string lnk, string target, string args, string desc, string icon = null)
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sc = sh.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.Arguments = args;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.Description = desc;
            sc.IconLocation = icon ?? IconLocation;
            sc.Save();
        }

        // Значок для ярлыков: отдельный .ico рядом с программой (у него новое имя при каждой установке,
        // поэтому Windows не показывает старую картинку из своего кэша), иначе — значок из exe.
        public static string IconLocation
        {
            get
            {
                try
                {
                    string ico = Directory.GetFiles(AppDir, "StartPack-*.ico").OrderByDescending(f => f).FirstOrDefault();
                    if (ico != null) return ico + ",0";
                }
                catch { }
                return Application.ExecutablePath + ",0";
            }
        }

        public static string GuessProcess(string raw, string kind)
        {
            if (kind == "zapret") return "winws";
            string p = Expand(raw);
            if (p.Length == 0 || IsShell(p)) return "";
            string ext = (Path.GetExtension(p) ?? "").ToLowerInvariant();
            if (ext == ".lnk")
            {
                string t = ResolveLnk(p);
                return t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(t) : "";
            }
            if (ext == ".exe")
            {
                string n = Path.GetFileNameWithoutExtension(p);
                return n.Equals("Update", StringComparison.OrdinalIgnoreCase) ? "" : n;
            }
            return "";
        }

        public static string GuessSpecial(string raw)
        {
            string f = SafeFileName(Expand(raw));
            if (f.Equals("Happ.lnk", StringComparison.OrdinalIgnoreCase) || f.Equals("Happ.exe", StringComparison.OrdinalIgnoreCase)) return "happ";
            if (f.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase)) return "spotify";
            if (f.Equals("incy.exe", StringComparison.OrdinalIgnoreCase)) return "incy";
            if (f.Equals("Koala Clash.exe", StringComparison.OrdinalIgnoreCase)) return "koala";
            return "";
        }

        public static void ShellOpen(string what)
        {
            using (Process.Start(new ProcessStartInfo(what) { UseShellExecute = true })) { }
        }

        // --- процессы

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // Путь к exe процесса — работает и для запущенных от администратора.
        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        public static HashSet<string> RunningNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                try { set.Add(p.ProcessName); } catch { }
                p.Dispose();
            }
            return set;
        }

        public static string[] ProcNames(Item it)
        {
            return (it.ProcessName ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Regex.Replace(s.Trim(), @"\.exe$", "", RegexOptions.IgnoreCase))
                .Where(s => s.Length > 0).ToArray();
        }

        public static bool IsRunning(Item it, HashSet<string> running)
        {
            return ProcNames(it).Any(running.Contains);
        }

        public static void Launch(Item it)
        {
            string p = Expand(it.Path);
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (IsShell(p))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = p;
            }
            else
            {
                string ext = (Path.GetExtension(p) ?? "").ToLowerInvariant();
                if (ext == ".ps1")
                {
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + p + "\" " + it.Args;
                }
                else
                {
                    psi.FileName = p;
                    psi.Arguments = it.Args ?? "";
                }
                string dir = SafeDir(p);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                if (it.Admin) psi.Verb = "runas";
            }
            using (Process.Start(psi)) { }
        }

        // Перезапуск zapret: закрыть winws и запустить .bat — одним запросом прав администратора.
        public static void RestartZapret(string bat)
        {
            // драйвер WinDivert / служба zapret из другой (старой) папки — снять, иначе новый zapret работает со старым драйвером
            var foreign = ZapretCtl.Foreign(Path.GetDirectoryName(bat));
            if (foreign.Count > 0) Journal.Write("zapret", "снимаю " + ZapretCtl.Describe(foreign));
            ZapretCtl.ForgetServices();
            var psi = new ProcessStartInfo("cmd.exe",
                "/c \"taskkill /F /IM winws.exe >nul 2>&1 & " + ZapretCtl.ResetCmd(foreign) + "timeout /t 2 /nobreak >nul & call \"" + bat + "\"\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WorkingDirectory = Path.GetDirectoryName(bat);
            using (Process.Start(psi)) { }
        }

        // --- ярлыки: автозапуск и «Запустить всё» на рабочем столе

        public static string StartupLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Старт-пак.lnk"); }
        }

        public static string RunAllLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Старт-пак — запустить всё.lnk"); }
        }

        public static bool AutostartEnabled() { return File.Exists(StartupLnk); }
        public static bool RunAllShortcutExists() { return File.Exists(RunAllLnk); }

        public static void SetAutostart(bool on)
        {
            if (on) CreateShortcut(StartupLnk, Application.ExecutablePath, "/autorun", "Старт-пак: запустить всё");
            else if (File.Exists(StartupLnk)) File.Delete(StartupLnk);
        }

        public static void SetRunAllShortcut(bool on)
        {
            if (on) CreateShortcut(RunAllLnk, Application.ExecutablePath, "/autorun", "Запустить всё из Старт-пака одним двойным щелчком");
            else if (File.Exists(RunAllLnk)) File.Delete(RunAllLnk);
        }

        // --- версии

        public static string CleanTag(string tag)
        {
            if (tag == null) return "";
            var m = Regex.Match(tag, @"\d.*");
            return m.Success ? m.Value.Trim() : tag.Trim();
        }

        static List<KeyValuePair<long, string>> VerParts(string v)
        {
            var r = new List<KeyValuePair<long, string>>();
            if (string.IsNullOrEmpty(v)) return r;
            var m = Regex.Match(v, @"\d.*");
            if (!m.Success) return r;
            foreach (string seg in m.Value.Split('.'))
            {
                var mm = Regex.Match(seg, @"^(\d*)(.*)$");
                long num;
                long.TryParse(mm.Groups[1].Value, out num);
                r.Add(new KeyValuePair<long, string>(num, mm.Groups[2].Value.Trim()));
            }
            return r;
        }

        public static int CompareVersions(string a, string b)
        {
            var pa = VerParts(a);
            var pb = VerParts(b);
            int n = Math.Max(pa.Count, pb.Count);
            var zero = new KeyValuePair<long, string>(0, "");
            for (int i = 0; i < n; i++)
            {
                var x = i < pa.Count ? pa[i] : zero;
                var y = i < pb.Count ? pb[i] : zero;
                if (x.Key != y.Key) return x.Key.CompareTo(y.Key);
                int s = string.Compare(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);
                if (s != 0) return s;
            }
            return 0;
        }

        public static string NormalizeVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Regex.Match(s, @"\d+(\.\d+)*");
            if (!m.Success) return null;
            string v = m.Value;
            while (v.Count(c => c == '.') > 2 && v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
            return v;
        }

        public static string ZapretVersion(string dir)
        {
            try
            {
                string svc = Path.Combine(dir, "service.bat");
                if (File.Exists(svc))
                    foreach (string line in File.ReadLines(svc).Take(15))
                    {
                        var m = Regex.Match(line, "LOCAL_VERSION=([^\"\\s]+)");
                        if (m.Success) return m.Groups[1].Value;
                    }
            }
            catch { }
            var m2 = Regex.Match(Path.GetFileName(dir.TrimEnd('\\')), @"(\d+(\.\d+)*\w*)$");
            return m2.Success ? m2.Groups[1].Value : "?";
        }

        public static string GetLocalVersion(Item it)
        {
            string p = Expand(it.Path);
            if (it.UpdateKind == "zapret")
            {
                string dir = SafeDir(p);
                return string.IsNullOrEmpty(dir) ? "?" : ZapretVersion(dir);
            }
            if (IsShell(p) || !File.Exists(p)) return "?";
            string v = null;
            try
            {
                var fi = FileVersionInfo.GetVersionInfo(p);
                v = NormalizeVer(fi.ProductVersion) ?? NormalizeVer(fi.FileVersion);
            }
            catch { }
            // Если в exe нет номера версии (или он устарел), верим тому, что поставили сами.
            if (!string.IsNullOrEmpty(it.InstalledTag))
            {
                long len = 0;
                try { len = new FileInfo(p).Length; } catch { }
                if (len == it.InstalledSize && (v == null || CompareVersions(it.InstalledTag, v) > 0)) v = CleanTag(it.InstalledTag);
            }
            return v ?? "?";
        }

        // --- zapret: самая новая папка рядом

        public static string ZapretPrefix(string folderName)
        {
            var m = Regex.Match(folderName ?? "", @"^(.*?)(\d+(\.\d+)*\w*)$");
            return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
        }

        // Если на ПК есть zapret новее того, что в списке (в любой папке), — переключиться на него,
        // сохранив выбранную стратегию.
        public static bool AdoptNewestZapret(Item it)
        {
            if (it.UpdateKind != "zapret") return false;
            try
            {
                string p = Expand(it.Path);
                if (IsShell(p)) return false;
                string dir = p.Length > 0 ? SafeDir(p) : null;
                string bat = p.Length > 0 ? SafeFileName(p) : "";
                bool ok = dir != null && File.Exists(p);
                string cur = ok ? ZapretVersion(dir) : "";
                foreach (Detect.ZapretFound z in Detect.ZapretAll())
                {
                    if (ok && (ZapretCtl.SameDir(z.Root, dir) || CompareVersions(z.Version, cur) <= 0)) break;
                    string nb = Detect.ZapretBat(z.Root, bat);
                    if (nb == null) continue;
                    Journal.Write("поиск", "zapret: " + (ok ? Path.GetFileName(dir) + " (" + cur + ")" : "не найден") + " → " + z.Root + " (" + z.Version + ")");
                    it.Path = nb;
                    it.LocalVersion = null;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // --- GitHub

        public static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "StartPack/2.0";
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        public static async Task<Release> LatestRelease(string repo)
        {
            using (WebClient wc = NewClient())
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                string s = await wc.DownloadStringTaskAsync("https://api.github.com/repos/" + repo.Trim().Trim('/') + "/releases/latest");
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(s);
                var r = new Release { Tag = Convert.ToString(d["tag_name"]) };
                object assets;
                if (d.TryGetValue("assets", out assets) && assets is IEnumerable)
                    foreach (object o in (IEnumerable)assets)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null) continue;
                        r.Assets.Add(new KeyValuePair<string, string>(Convert.ToString(a["name"]), Convert.ToString(a["browser_download_url"])));
                    }
                return r;
            }
        }

        public static string PickAsset(Item it, Release r)
        {
            foreach (var a in r.Assets)
            {
                if (it.UpdateKind == "zapret")
                {
                    if (a.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return a.Value;
                }
                else if (it.Asset.Trim().Length > 0)
                {
                    if (a.Key.Equals(it.Asset.Trim(), StringComparison.OrdinalIgnoreCase)) return a.Value;
                }
                else if (a.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return a.Value;
            }
            return null;
        }

        // --- установка новой версии zapret

        static string FindDirWith(string root, string file, int depth)
        {
            if (File.Exists(Path.Combine(root, file))) return root;
            if (depth <= 0) return null;
            foreach (string d in Directory.GetDirectories(root))
            {
                string r = FindDirWith(d, file, depth - 1);
                if (r != null) return r;
            }
            return null;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // Возвращает папку, куда поставлена новая версия.
        public static string InstallZapret(string zip, string oldDir, string newDir)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "StartPack", "x" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                ZipFile.ExtractToDirectory(zip, tmp);
                string root = FindDirWith(tmp, "service.bat", 3);
                if (root == null) throw new Exception("в архиве нет service.bat — похоже, формат релиза изменился");
                string target = newDir;
                for (int n = 2; Directory.Exists(target); n++) target = newDir + " (" + n + ")";
                try
                {
                    CopyDir(root, target);
                    if (!string.IsNullOrEmpty(oldDir) && Directory.Exists(oldDir)) MigrateZapret(oldDir, target);
                }
                catch
                {
                    // не оставлять полуготовую папку — старая версия остаётся рабочей
                    try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { }
                    throw;
                }
                return target;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                try { File.Delete(zip); } catch { }
            }
        }

        // Переносит ваши настройки: свои списки, флажки (игровой фильтр, проверка обновлений), режим ipset.
        public static void MigrateZapret(string oldDir, string newDir)
        {
            string ol = Path.Combine(oldDir, "lists"), nl = Path.Combine(newDir, "lists");
            if (Directory.Exists(ol))
            {
                Directory.CreateDirectory(nl);
                foreach (string f in Directory.GetFiles(ol, "*-user.txt"))
                    File.Copy(f, Path.Combine(nl, Path.GetFileName(f)), true);
            }
            string ou = Path.Combine(oldDir, "utils"), nu = Path.Combine(newDir, "utils");
            if (Directory.Exists(ou) && Directory.Exists(nu))
            {
                var oldFlags = new HashSet<string>(Directory.GetFiles(ou, "*.enabled").Select(f => Path.GetFileName(f)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(nu, "*.enabled"))
                    if (!oldFlags.Contains(Path.GetFileName(f))) File.Delete(f);
                foreach (string f in Directory.GetFiles(ou, "*.enabled"))
                    File.Copy(f, Path.Combine(nu, Path.GetFileName(f)), true);
            }
            ApplyIpsetMode(oldDir, newDir);
        }

        public static string IpsetStatus(string file)
        {
            if (!File.Exists(file)) return "missing";
            string t = File.ReadAllText(file);
            if (t.Length == 0) return "any";
            return t.Contains(IpsetNone) ? "none" : "loaded";
        }

        static void ApplyIpsetMode(string oldDir, string newDir)
        {
            string ol = Path.Combine(oldDir, "lists", "ipset-all.txt");
            string nl = Path.Combine(newDir, "lists", "ipset-all.txt");
            string nb = nl + ".backup";
            string os = IpsetStatus(ol), ns = IpsetStatus(nl);
            if (os == "missing" || ns == "missing" || os == ns) return;
            if (os == "loaded")
            {
                if (File.Exists(nb)) { File.Delete(nl); File.Move(nb, nl); }
                else File.Copy(ol, nl, true);
                return;
            }
            if (ns == "loaded")
            {
                if (File.Exists(nb)) File.Delete(nb);
                File.Move(nl, nb);
            }
            File.WriteAllText(nl, os == "none" ? IpsetNone + "\r\n" : "");
        }

        // --- замена одного exe

        public static void ReplaceFile(string downloaded, string target)
        {
            string old = target + ".old";
            if (File.Exists(old)) File.Delete(old);
            if (File.Exists(target)) File.Move(target, old);
            try { File.Move(downloaded, target); }
            catch
            {
                if (File.Exists(old) && !File.Exists(target)) File.Move(old, target);
                throw;
            }
            try { File.Delete(old); } catch { }
        }

        // --- приложения из меню «Пуск» (в т. ч. из Microsoft Store)

        public static List<KeyValuePair<string, string>> StartApps()
        {
            var r = new List<KeyValuePair<string, string>>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic it in folder.Items())
                {
                    string name = Convert.ToString(it.Name);
                    string path = Convert.ToString(it.Path);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path)) r.Add(new KeyValuePair<string, string>(name, path));
                }
            }
            catch { }
            return r.OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string StartAppToPath(string appPath)
        {
            try { if (File.Exists(appPath)) return appPath; } catch { }
            return "shell:AppsFolder\\" + appPath;
        }

        // --- запущенные программы (для «Добавить из запущенных»)

        public class RunningApp
        {
            public string Name, Path, Title;
            public bool HasWindow, System;
        }

        public static List<RunningApp> RunningApps()
        {
            var byPath = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4) continue;
                    string path = ProcessPath(p.Id);
                    if (string.IsNullOrEmpty(path)) continue;
                    bool hasWin = false;
                    try { hasWin = p.MainWindowHandle != IntPtr.Zero; } catch { }
                    RunningApp a;
                    if (!byPath.TryGetValue(path, out a))
                    {
                        string title = null;
                        try
                        {
                            var fi = FileVersionInfo.GetVersionInfo(path);
                            title = !string.IsNullOrWhiteSpace(fi.FileDescription) ? fi.FileDescription.Trim() : fi.ProductName;
                        }
                        catch { }
                        a = new RunningApp
                        {
                            Name = p.ProcessName, Path = path,
                            Title = string.IsNullOrWhiteSpace(title) ? p.ProcessName : title.Trim(),
                            System = path.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                        };
                        byPath[path] = a;
                    }
                    if (hasWin) a.HasWindow = true;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return byPath.Values
                .OrderByDescending(a => a.HasWindow)
                .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    // ---------------------------------------------------------------- поиск программ на этом ПК

    public static class Detect
    {
        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        static string Downloads { get { return Path.Combine(Home, "Downloads"); } }

        public static string[] Roots
        {
            get { return new[] { Desktop, Downloads, Path.Combine(Home, "Documents"), Home }; }
        }

        public static Config Defaults()
        {
            var c = new Config { Version = Core.ConfigVersion };
            Templates.AddStandard(c);
            c.EnsureProfiles();
            return c;
        }

        // Самый новый zapret на этом ПК (папка с general (ALT).bat или general.bat).
        public static string Zapret()
        {
            foreach (ZapretFound z in ZapretAll())
            {
                string bat = ZapretBat(z.Root, null);
                if (bat != null) return bat;
            }
            return null;
        }

        public static string ZapretBat(string root, string prefer)
        {
            foreach (string b in new[] { prefer, "general (ALT).bat", "general.bat" })
                if (!string.IsNullOrEmpty(b) && File.Exists(Path.Combine(root, b))) return Path.Combine(root, b);
            try { return Directory.GetFiles(root, "general*.bat").OrderBy(f => f.Length).FirstOrDefault(); } catch { return null; }
        }

        public class ZapretFound { public string Root, Version; public DateTime Time; }

        static List<ZapretFound> zapretCache;
        static DateTime zapretTime;

        public static void ForgetZapret() { zapretCache = null; }

        // Все папки zapret на ПК — по содержимому (service.bat + bin\winws.exe), а не по названию:
        // рабочий стол (и с OneDrive), загрузки, документы, папка пользователя, корни дисков — на 2 уровня вглубь;
        // плюс папка запущенного winws и папки, на которые указывают службы zapret.
        // Порядок: сначала самая новая версия, при равных — самая свежая папка.
        public static List<ZapretFound> ZapretAll()
        {
            if (zapretCache != null && (DateTime.UtcNow - zapretTime).TotalSeconds < 20) return zapretCache;
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = d =>
            {
                try { if (!string.IsNullOrEmpty(d) && IsZapretRoot(d)) roots.Add(Path.GetFullPath(d).TrimEnd('\\')); } catch { }
            };
            var places = new List<string>(Roots)
            {
                Path.Combine(Home, "Desktop"), Path.Combine(Home, "OneDrive", "Desktop"), Path.Combine(Home, "OneDrive", "Рабочий стол"),
                Path.Combine(Home, "OneDrive", "Documents"), Templates.AppsDir,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            foreach (string place in places.Distinct(StringComparer.OrdinalIgnoreCase))
                Scan(place, 2, add);
            try
            {
                // корни дисков — неглубоко: D:\zapret-…, D:\Programs\zapret-…
                foreach (DriveInfo d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                    Scan(d.RootDirectory.FullName, 1, add);
            }
            catch { }
            try
            {
                foreach (Process pr in Process.GetProcessesByName("winws"))
                {
                    try { add(Core.SafeDir(Core.SafeDir(Core.ProcessPath(pr.Id)))); } catch { }
                    pr.Dispose();
                }
            }
            catch { }
            foreach (ZapretCtl.Svc s in ZapretCtl.Services()) add(s.Root);

            zapretCache = roots.Select(r =>
            {
                DateTime t = DateTime.MinValue;
                try { t = File.GetLastWriteTimeUtc(Path.Combine(r, "service.bat")); } catch { }
                return new ZapretFound { Root = r, Version = Core.ZapretVersion(r), Time = t };
            })
            .OrderByDescending(z => z, Comparer<ZapretFound>.Create((a, b) =>
            {
                int c = Core.CompareVersions(a.Version, b.Version);
                return c != 0 ? c : a.Time.CompareTo(b.Time);
            }))
            .ToList();
            zapretTime = DateTime.UtcNow;
            return zapretCache;
        }

        static bool IsZapretRoot(string d)
        {
            return File.Exists(Path.Combine(d, "service.bat")) && File.Exists(Path.Combine(d, "bin", "winws.exe"));
        }

        static readonly string[] SkipDirs = { "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "AppData", "node_modules", ".git", "WindowsApps", "Recovery", "PerfLogs" };

        static void Scan(string dir, int depth, Action<string> add)
        {
            if (depth < 0) return;
            string[] subs;
            try
            {
                if (!Directory.Exists(dir)) return;
                subs = Directory.GetDirectories(dir);
            }
            catch { return; }
            foreach (string s in subs)
            {
                string name = Path.GetFileName(s);
                if (SkipDirs.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;   // ссылки и «Мои документы» старого вида
                }
                catch { continue; }
                if (IsZapretRoot(s)) { add(s); continue; }
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                Scan(s, depth - 1, add);
            }
        }

        public static string TgWs()
        {
            string tg = Path.Combine(Downloads, "TgWsProxy_windows.exe");
            if (File.Exists(tg)) return tg;
            string mine = Path.Combine(Templates.AppsDir, "TgWsProxy_windows.exe");
            if (File.Exists(mine)) return mine;
            // скачанный установленным Старт-паком (для переносной копии и после переустановки)
            string inst = Path.Combine(Setup.InstallDir, "apps", "TgWsProxy_windows.exe");
            if (File.Exists(inst)) return inst;
            return NewestFile(new[] { Downloads, Desktop }, "TgWsProxy*.exe");
        }

        public static string Happ()
        {
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                string happ = Path.Combine(root, "Happ - Proxy Utility", "Happ.lnk");
                if (File.Exists(happ)) return happ;
            }
            return null;
        }

        public static string StartApp(params string[] names)
        {
            return Core.StartApps().Where(a => names.Any(n => a.Key.Equals(n, StringComparison.OrdinalIgnoreCase) || a.Key.StartsWith(n + " ", StringComparison.OrdinalIgnoreCase)))
                .Select(a => Core.StartAppToPath(a.Value)).FirstOrDefault();
        }

        public static string Telegram()
        {
            string p = StartApp("Telegram Desktop", "Telegram");
            if (p != null) return p;
            string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe");
            return File.Exists(classic) ? classic : null;
        }

        public static string Discord()
        {
            string up = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Discord", "Update.exe");
            return File.Exists(up) ? up : null;
        }

        public static string Incy()
        {
            foreach (string f in new[]
            {
                Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs", "INCY", "incy.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs", "incy", "incy.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "INCY", "incy.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "incy", "incy.exe")
            })
                if (File.Exists(f)) return f;
            return StartApp("INCY", "Incy", "incy");
        }

        public static string Warp()
        {
            return File.Exists(WarpCtl.Exe) ? WarpCtl.Exe : StartApp("Cloudflare WARP");
        }

        public static string Koala()
        {
            string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Koala Clash", "Koala Clash.exe");
            if (File.Exists(f)) return f;
            string u = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs", "Koala Clash", "Koala Clash.exe");
            if (File.Exists(u)) return u;
            return StartApp("Koala Clash");
        }

        public static string Spotify()
        {
            string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
            if (File.Exists(classic)) return classic;
            return StartApp("Spotify");
        }

        static string NewestFile(string[] roots, string mask)
        {
            FileInfo best = null;
            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string f in Directory.GetFiles(root, mask))
                    {
                        var fi = new FileInfo(f);
                        if (best == null || fi.LastWriteTimeUtc > best.LastWriteTimeUtc) best = fi;
                    }
                }
                catch { }
            }
            return best == null ? null : best.FullName;
        }
    }

    // ---------------------------------------------------------------- готовые пункты

    public static class Templates
    {
        public static string AppsDir { get { return Path.Combine(Core.AppDir, "apps"); } }

        public static Item Zapret()
        {
            string p = Detect.Zapret();
            return p == null ? null : ZapretAt(p);
        }

        public static Item ZapretAt(string bat)
        {
            return new Item
            {
                Name = "zapret (Discord и YouTube)", Path = bat, ProcessName = "winws", Admin = true, Delay = 2, Watch = true,
                UpdateKind = "zapret", Repo = "Flowseal/zapret-discord-youtube"
            };
        }

        public static Item TgWs()
        {
            string p = Detect.TgWs();
            return p == null ? null : TgWsAt(p);
        }

        public static Item TgWsAt(string exe)
        {
            return new Item
            {
                Name = "TG WS Proxy", Path = exe, ProcessName = Path.GetFileNameWithoutExtension(exe), Delay = 1, Watch = true,
                UpdateKind = "github-exe", Repo = "Flowseal/tg-ws-proxy", Asset = "TgWsProxy_windows.exe"
            };
        }

        public static Item Happ() { string p = Detect.Happ(); return p == null ? null : Make("happ", p); }
        public static Item Incy() { string p = Detect.Incy(); return p == null ? null : Make("incy", p); }
        public static Item Koala() { string p = Detect.Koala(); return p == null ? null : Make("koala", p); }
        public static Item Warp() { string p = Detect.Warp(); return p == null ? null : Make("warp", p); }
        public static Item Spotify() { string p = Detect.Spotify(); return p == null ? null : Make("spotify", p); }
        public static Item Telegram() { string p = Detect.Telegram(); return p == null ? null : Make("telegram", p); }
        public static Item Discord() { string p = Detect.Discord(); return p == null ? null : Make("discord", p); }

        // Карточка известной программы; path может быть пустым — тогда это заготовка «не установлена» с кнопкой «Установить».
        public static Item Make(string kind, string path)
        {
            path = path ?? "";
            switch (kind)
            {
                case "zapret": return ZapretAt(path);
                case "tgws":
                    Item tg = TgWsAt(path);
                    if (path.Length == 0) tg.ProcessName = "TgWsProxy_windows";
                    return tg;
                case "happ": return new Item { Name = "Happ", Path = path, ProcessName = "Happ", Delay = 1, Special = "happ", HappAutoConnect = true };
                case "incy": return new Item { Name = "INCY", Path = path, ProcessName = "incy", Delay = 1, Special = "incy", HappAutoConnect = true };
                case "koala": return new Item { Name = "Koala Clash", Path = path, ProcessName = "Koala Clash", Delay = 1, Special = "koala", HappAutoConnect = true };
                case "warp": return new Item { Name = "Cloudflare WARP", Path = path, ProcessName = "Cloudflare WARP", Delay = 1, Special = "warp", HappAutoConnect = true };
                case "spotify":
                    return new Item
                    {
                        Name = "Spotify", Path = path, ProcessName = "Spotify", Special = "spotify",
                        SpotifyCheckHosts = true, HostsFixDir = HostsFix.Find() ?? ""
                    };
                case "telegram": return new Item { Name = "Telegram", Path = path, ProcessName = "Telegram" };
                case "discord": return new Item { Name = "Discord", Path = path, Args = "--processStart Discord.exe", ProcessName = "Discord" };
            }
            return null;
        }

        public static readonly string[] StandardKinds = { "zapret", "tgws", "spotify", "telegram", "discord", "warp", "happ", "incy", "koala" };

        // Стандартный набор: все известные программы. Найденные на ПК — с путём, остальные — заготовки «не установлена».
        // VPN всегда выключены (в «Запустить всё» не входят): какой из них нужен, человек включает сам.
        // Возвращает добавленные карточки (тех, что уже есть в списке, не дублирует).
        public static List<Item> AddStandard(Config c)
        {
            var added = new List<Item>();
            foreach (string kind in StandardKinds)
            {
                if (c.Items.Any(x => Installers.KindOf(x) == kind)) continue;
                AppSource a = Installers.Get(kind);
                string found = a == null ? null : Installers.SafeDetect(a);
                Item it = Make(kind, found);
                if (it == null) continue;
                if (it.UpdateKind.Length > 0) it.Watch = found != null;
                it.Enabled = found != null && !VpnCtl.IsVpn(it);
                c.Items.Add(it);
                added.Add(it);
            }
            VpnCtl.SortLast(c);
            return added;
        }
    }
}
