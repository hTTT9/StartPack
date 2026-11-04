// Сеть: загрузки (с проверкой SHA-256 из релиза GitHub), проверка связи с сервисами, релизы GitHub.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace StartPack.Linux;

public class NetTarget
{
    public string Name;
    public string Url;            // проверка по HTTPS (любой ответ сервера — значит, доступен)
    public int ProxyPort;         // Telegram: сначала — отвечает ли TG WS Proxy на этом ПК
    public string State = "";     // "", checking, ok, fail
    public int Ms = -1;
    public string Error;
}

public static class Net
{
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8), AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All
    })
    { Timeout = TimeSpan.FromSeconds(10) };

    static readonly HttpClient Dl = new() { Timeout = TimeSpan.FromMinutes(15) };

    static Net()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("StartPack-Linux/" + App.Version);
        Dl.DefaultRequestHeaders.UserAgent.ParseAdd("StartPack-Linux/" + App.Version);
    }

    // ---------------------------------------------------------------- проверка связи

    public static List<NetTarget> TargetsFor(Config c)
    {
        var l = new List<NetTarget>();
        if (c.Items.Any(i => i.Kind is "zapret" or "discord"))
        {
            l.Add(new NetTarget { Name = "YouTube", Url = "https://www.youtube.com/generate_204" });
            l.Add(new NetTarget { Name = "Discord", Url = "https://discord.com/api/v9/gateway" });
        }
        if (c.Items.Any(i => i.Kind == "spotify")) l.Add(new NetTarget { Name = "Spotify", Url = "https://apresolve.spotify.com/?type=accesspoint" });
        if (c.Items.Any(i => i.Kind == "tgws")) l.Add(new NetTarget { Name = "Telegram", ProxyPort = TgWsPort(), Url = "https://web.telegram.org/" });
        return l;
    }

    // Порт TG WS Proxy — из его настроек; по умолчанию 1443.
    public static int TgWsPort()
    {
        foreach (string f in new[] { Path.Combine(Store.Home, ".config", "TgWsProxy", "config.json"), Path.Combine(Store.Home, ".config", "tg-ws-proxy", "config.json") })
        {
            try
            {
                if (!File.Exists(f)) continue;
                using var d = JsonDocument.Parse(File.ReadAllText(f));
                if (d.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out int port)) return port;
            }
            catch { }
        }
        return 1443;
    }

    public static async Task Check(NetTarget t)
    {
        t.State = "checking";
        var sw = Stopwatch.StartNew();
        try
        {
            if (t.ProxyPort > 0)
            {
                using var c = new TcpClient();
                var conn = c.ConnectAsync(IPAddress.Loopback, t.ProxyPort);
                if (await Task.WhenAny(conn, Task.Delay(3000)) != conn || !c.Connected) throw new Exception("TG WS Proxy не отвечает на 127.0.0.1:" + t.ProxyPort + " — он запущен?");
                sw.Restart();
            }
            if (t.Url != null)
            {
                try
                {
                    using var resp = await Http.GetAsync(t.Url, HttpCompletionOption.ResponseHeadersRead);
                }
                catch (HttpRequestException ex) when (ex.StatusCode != null) { /* любой HTTP-ответ — сайт доступен */ }
            }
            t.Ms = (int)sw.ElapsedMilliseconds;
            t.State = "ok";
            t.Error = null;
        }
        catch (Exception ex)
        {
            t.State = "fail";
            t.Error = ex is TaskCanceledException ? "нет ответа (тайм-аут)" : ex.InnerException?.Message ?? ex.Message;
        }
        // Telegram без VPN напрямую часто заблокирован, а через TG WS Proxy работает — тогда честно «работает» без цифры
        if (t.ProxyPort > 0 && t.State == "fail" && !(t.Error ?? "").StartsWith("TG WS Proxy"))
        {
            t.State = "ok"; t.Ms = -1; t.Error = null;
        }
    }

    // ---------------------------------------------------------------- загрузки

    public static async Task Download(string url, string file, IProgress<int> progress = null)
    {
        using var resp = await Dl.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(file);
        var buf = new byte[81920];
        long done = 0; int read, last = -1;
        while ((read = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read));
            done += read;
            if (total > 0 && progress != null) { int pct = (int)(done * 100 / total); if (pct != last) { last = pct; progress.Report(pct); } }
        }
    }

    public static string Sha256(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- релизы GitHub

    public record Asset(string Name, string Url, string Sha256, long Size);
    public record Release(string Tag, string Notes, string Page, List<Asset> Assets);

    static Release Parse(JsonElement r)
    {
        var assets = new List<Asset>();
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            string dg = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            assets.Add(new Asset(a.GetProperty("name").GetString(), a.GetProperty("browser_download_url").GetString(),
                dg != null && dg.StartsWith("sha256:") ? dg[7..].ToLowerInvariant() : null, a.GetProperty("size").GetInt64()));
        }
        return new Release(r.GetProperty("tag_name").GetString(),
            r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "",
            r.GetProperty("html_url").GetString(), assets);
    }

    public static async Task<Release> Latest(string repo)
    {
        string json = await Http.GetStringAsync("https://api.github.com/repos/" + repo + "/releases/latest");
        using var d = JsonDocument.Parse(json);
        return Parse(d.RootElement);
    }

    // Самый свежий выпуск с меткой, начинающейся с prefix (у Старт-пака Linux-выпуски — linux-v…).
    public static async Task<Release> LatestWithPrefix(string repo, string prefix)
    {
        string json = await Http.GetStringAsync("https://api.github.com/repos/" + repo + "/releases?per_page=30");
        using var d = JsonDocument.Parse(json);
        foreach (var r in d.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var dr) && dr.GetBoolean()) continue;
            if (r.GetProperty("tag_name").GetString().StartsWith(prefix)) return Parse(r);
        }
        return null;
    }

    // Скачать файл из релиза и сверить SHA-256, которую публикует GitHub. Не совпала — файл удаляется.
    public static async Task<string> DownloadVerified(Asset a, string file, IProgress<int> progress = null)
    {
        await Download(a.Url, file, progress);
        if (a.Sha256 == null) { File.Delete(file); throw new Exception("у файла " + a.Name + " в релизе нет контрольной суммы — ставить такой файл небезопасно"); }
        string got = Sha256(file);
        if (got != a.Sha256) { File.Delete(file); throw new Exception("контрольная сумма " + a.Name + " не совпала — файл повреждён или подменён"); }
        return file;
    }

    public static int CompareVersions(string a, string b)
    {
        int[] P(string s) => System.Text.RegularExpressions.Regex.Matches(s ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        int[] x = P(a), y = P(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int u = i < x.Length ? x[i] : 0, v = i < y.Length ? y[i] : 0;
            if (u != v) return u.CompareTo(v);
        }
        return 0;
    }
}
