// Известные программы на Linux: где искать, как запустить, как поставить.
// Установка — только официальными путями: Flathub, репозитории дистрибутива, релизы разработчиков на GitHub (с проверкой SHA-256).

namespace StartPack.Linux;

public class Known
{
    public string Kind, Name, About, Process, Page, PageHint;
    public string Flatpak;                 // id на Flathub
    public string[] Binaries = Array.Empty<string>();   // где искать (имя в PATH или полный путь)
    public string AptPackage, DnfPackage;  // пакет в репозиториях дистрибутива
    public string GithubRepo, DebAsset, RpmAsset, BinAsset;   // релизы разработчика на GitHub
}

public static class Catalog
{
    static string H(params string[] p) => Path.Combine(new[] { Store.Home }.Concat(p).ToArray());

    public static readonly List<Known> All = new()
    {
        new Known
        {
            Kind = "zapret", Name = "zapret (Discord и YouTube)", About = "обход блокировок — перенос стратегий Flowseal для Linux",
            Process = "nfqws", Page = "https://github.com/Sergeydigl3/zapret-discord-youtube-linux",
            PageHint = "Старт-пак ставит его сам в свою папку. Нужны git, curl и nftables — их он поставит из репозиториев системы."
        },
        new Known
        {
            Kind = "tgws", Name = "TG WS Proxy", About = "прокси для Telegram", Process = "tg-ws-proxy,TgWsProxy_linux_amd64,TgWsProxy_linux",
            Binaries = new[] { "tg-ws-proxy", Path.Combine(Store.DataDir, "apps", "tg-ws-proxy"), H("Downloads", "TgWsProxy_linux_amd64"), H("Загрузки", "TgWsProxy_linux_amd64") },
            GithubRepo = "Flowseal/tg-ws-proxy", BinAsset = "TgWsProxy_linux_amd64",
            Page = "https://github.com/Flowseal/tg-ws-proxy/releases/latest", PageHint = "Скачайте TgWsProxy_linux_amd64.deb (Ubuntu, Debian) или .rpm (Fedora, РЕД ОС) и установите."
        },
        new Known
        {
            Kind = "spotify", Name = "Spotify", About = "музыка", Process = "spotify", Flatpak = "com.spotify.Client",
            Binaries = new[] { "spotify" },
            Page = "https://www.spotify.com/download/linux/", PageHint = "Удобнее всего — через Flathub или Snap, как написано на странице."
        },
        new Known
        {
            Kind = "telegram", Name = "Telegram", About = "мессенджер", Process = "telegram-desktop,Telegram", Flatpak = "org.telegram.desktop",
            Binaries = new[] { "telegram-desktop", "Telegram", H("Telegram", "Telegram"), H("Downloads", "Telegram", "Telegram"), H("Загрузки", "Telegram", "Telegram"), "/opt/telegram/Telegram" },
            AptPackage = "telegram-desktop",
            Page = "https://desktop.telegram.org/", PageHint = "Скачайте архив для Linux, распакуйте и запустите Telegram — Старт-пак найдёт его в домашней папке или «Загрузках»."
        },
        new Known
        {
            Kind = "discord", Name = "Discord", About = "голос и чаты", Process = "Discord,discord", Flatpak = "com.discordapp.Discord",
            Binaries = new[] { "discord", "/opt/discord/Discord", "/usr/share/discord/Discord" },
            Page = "https://discord.com/download", PageHint = "Скачайте .deb (Ubuntu, Debian) или .tar.gz и установите."
        },
        new Known
        {
            Kind = "warp", Name = "Cloudflare WARP", About = "бесплатный VPN от Cloudflare", Process = "warp-taskbar",
            Binaries = new[] { "warp-cli" },
            Page = "https://pkg.cloudflareclient.com/", PageHint = "Выберите свою систему и выполните команды со страницы — они подключат репозиторий Cloudflare и поставят WARP."
        },
        new Known
        {
            Kind = "happ", Name = "Happ", About = "VPN-клиент", Process = "Happ",
            Binaries = new[] { "/opt/happ/bin/Happ", "happ" },
            GithubRepo = "Happ-proxy/happ-desktop", DebAsset = "Happ.linux.x64.deb", RpmAsset = "Happ.linux.x64.rpm",
            Page = "https://github.com/Happ-proxy/happ-desktop/releases/latest", PageHint = "Скачайте Happ.linux.x64.deb (Ubuntu, Debian) или .rpm (Fedora, РЕД ОС) и установите."
        },
    };

    public static Known Get(string kind) => All.FirstOrDefault(k => k.Kind == kind);

    // Что запускать: путь к программе или «flatpak:id». null — не установлена.
    public static string Detect(Known k)
    {
        if (k.Kind == "zapret") return Zapret.Installed ? Zapret.Dir : null;
        foreach (string b in k.Binaries)
        {
            string f = Sys.Which(b);
            if (f != null) return f;
        }
        if (k.Flatpak != null && Sys.FlatpakInstalled(k.Flatpak)) return "flatpak:" + k.Flatpak;
        return null;
    }

    public static Item Make(Known k, string command)
    {
        return new Item
        {
            Kind = k.Kind, Name = k.Name, Command = command ?? "", Process = k.Process,
            Delay = k.Kind == "zapret" ? 2 : 1,
            Enabled = command != null && k.Kind is not ("happ" or "warp"),   // VPN — выключены, пока человек не включит сам
        };
    }

    // Все известные программы: найденные — с путём, остальные — заготовки с кнопкой «Установить».
    public static List<Item> StandardSet() => All.Select(k => Make(k, Detect(k))).ToList();

    // Карточка ещё не знает, где программа, — поискать снова (поставили сами, через Старт-пак или из магазина).
    public static bool Refresh(Item it)
    {
        Known k = Get(it.Kind);
        if (k == null) return false;
        string now = Detect(k);
        if (now == null || now == it.Command) return false;
        bool wasEmpty = string.IsNullOrEmpty(it.Command);
        it.Command = now;
        if (wasEmpty && !it.IsVpn) it.Enabled = true;
        return true;
    }

    public static bool Exists(Item it)
    {
        if (it.Kind == "zapret") return Zapret.Installed;
        if (string.IsNullOrEmpty(it.Command)) return false;
        if (it.Command.StartsWith("flatpak:")) return Sys.FlatpakInstalled(it.Command[8..]);
        return File.Exists(it.Command) || Sys.Which(it.Command) != null;
    }
}
