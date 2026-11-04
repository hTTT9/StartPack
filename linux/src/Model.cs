// Список программ и настройки: ~/.config/startpack/config.json (JSON, читается и правится руками).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartPack.Linux;

public class Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Kind { get; set; } = "";        // zapret, tgws, telegram, discord, spotify, happ, warp или "" — своя программа
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";     // путь к программе, «flatpak:org.id» или пусто (zapret — своё управление)
    public string Args { get; set; } = "";
    public string Process { get; set; } = "";     // имена процессов через запятую — по ним видно, что программа работает
    public bool Enabled { get; set; } = true;     // участвует в «Запустить всё»
    public int Delay { get; set; } = 1;           // пауза после запуска, сек
    public bool AutoConnect { get; set; } = true; // VPN: подключать после запуска

    [JsonIgnore] public string Message { get; set; }
    [JsonIgnore] public bool MessageIsError { get; set; }
    [JsonIgnore] public bool Busy { get; set; }

    public bool IsVpn => Kind is "happ" or "warp";
}

public class ZapretSettings
{
    public string Strategy { get; set; } = "general_alt.bat";
    public string Interface { get; set; } = "any";
    public bool GameFilterTcp { get; set; }
    public bool GameFilterUdp { get; set; }
}

public class Config
{
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "dark";          // dark, light, system
    public bool PauseZapretForVpn { get; set; } = true;  // пока подключён VPN, zapret выключен
    public bool CheckUpdates { get; set; } = true;
    public bool NetCheck { get; set; } = true;
    public ZapretSettings Zapret { get; set; } = new();
    public List<Item> Items { get; set; } = new();
}

public static class Store
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Home => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigDir
    {
        get
        {
            string x = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(string.IsNullOrEmpty(x) ? Path.Combine(Home, ".config") : x, "startpack");
        }
    }

    public static string DataDir
    {
        get
        {
            string x = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(string.IsNullOrEmpty(x) ? Path.Combine(Home, ".local", "share") : x, "startpack");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static bool FirstRun { get; private set; }

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                Config c = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), Opts);
                if (c != null)
                {
                    c.Items ??= new List<Item>();
                    c.Items.RemoveAll(i => i == null);
                    c.Zapret ??= new ZapretSettings();
                    return c;
                }
            }
        }
        catch (Exception ex)
        {
            try { File.Copy(ConfigPath, ConfigPath + ".broken", true); } catch { }
            Log.Write("настройки", "не прочитались: " + ex.Message + " — собраны заново");
        }
        FirstRun = true;
        Config fresh = new() { Items = Catalog.StandardSet() };
        Save(fresh);
        return fresh;
    }

    public static void Save(Config c)
    {
        Directory.CreateDirectory(ConfigDir);
        string tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, Opts));
        File.Move(tmp, ConfigPath, true);
    }
}

// Журнал: ~/.local/share/startpack/startpack.log (последние строки — и в окне «Журнал»)
public static class Log
{
    static readonly object Sync = new();
    public static string FilePath => Path.Combine(Store.DataDir, "startpack.log");

    public static void Write(string area, string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + area + "] " + (text ?? "").Replace('\n', ' ');
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Store.DataDir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024) File.Move(FilePath, FilePath + ".1", true);
                File.AppendAllText(FilePath, line + "\n");
            }
            catch { }
        }
        Added?.Invoke(line);
    }

    public static event Action<string> Added;
}
