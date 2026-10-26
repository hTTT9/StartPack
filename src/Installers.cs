// Установка недостающих программ с официальных сайтов и репозиториев.
// Скачанный установщик запускается, только если у него верная цифровая подпись нужного издателя.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace StartPack
{
    public class AppSource
    {
        public string Id, Name, About, Url, FileName, Publisher, Host;
        public string Sha256From;          // "sums:<url SHA256SUMS>" или "github:<owner/repo>" — для неподписанных установщиков
        public Func<string> Detect;          // путь, если программа уже установлена
        public Func<Item> Template;          // карточка для списка
        public string Page;                  // официальная страница загрузки (если автоматическая установка не вышла)
        public string PageHint;              // что там нажать — коротко
    }

    public static class Installers
    {
        public static readonly List<AppSource> All = new List<AppSource>
        {
            new AppSource { Id = "zapret", Page = "https://github.com/Flowseal/zapret-discord-youtube/releases/latest", PageHint = "Скачайте архив zapret-discord-youtube-….zip (раздел Assets), распакуйте в любую папку, например на рабочий стол.", Name = "zapret", About = "обход блокировок Discord и YouTube", Host = "github.com/Flowseal",
                Detect = StartPack.Detect.Zapret, Template = Templates.Zapret },
            new AppSource { Id = "tgws", Page = "https://github.com/Flowseal/tg-ws-proxy/releases/latest", PageHint = "Скачайте файл TgWsProxy_windows.exe (раздел Assets) и положите в «Загрузки» — Старт-пак найдёт его сам.", Name = "TG WS Proxy", About = "прокси для Telegram", Host = "github.com/Flowseal",
                Detect = StartPack.Detect.TgWs, Template = Templates.TgWs },
            new AppSource { Id = "spotify", Page = "https://www.spotify.com/download/windows/", PageHint = "Нажмите «Скачать» и запустите скачанный SpotifySetup.exe.", Name = "Spotify", About = "музыка", Host = "spotify.com",
                Url = "https://download.scdn.co/SpotifySetup.exe", FileName = "SpotifySetup.exe",
                Publisher = "Spotify AB", Detect = StartPack.Detect.Spotify, Template = Templates.Spotify },
            new AppSource { Id = "telegram", Page = "https://desktop.telegram.org/", PageHint = "Нажмите «Get Telegram for Windows x64» и запустите установщик.", Name = "Telegram", About = "мессенджер", Host = "telegram.org",
                Url = "https://telegram.org/dl/desktop/win64", FileName = "tsetup-x64.exe",
                Publisher = "Telegram FZ-LLC", Detect = StartPack.Detect.Telegram, Template = Templates.Telegram },
            new AppSource { Id = "discord", Page = "https://discord.com/download", PageHint = "Нажмите «Download for Windows» и запустите DiscordSetup.exe.", Name = "Discord", About = "голос и чаты", Host = "discord.com",
                Url = "https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64", FileName = "DiscordSetup.exe",
                Publisher = "Discord Inc.", Detect = StartPack.Detect.Discord, Template = Templates.Discord },
            new AppSource { Id = "happ", Page = "https://github.com/Happ-proxy/happ-desktop/releases/latest", PageHint = "Скачайте setup-Happ.x64.exe (раздел Assets) и запустите.", Name = "Happ", About = "VPN-клиент", Host = "github.com/Happ-proxy",
                Url = "https://github.com/Happ-proxy/happ-desktop/releases/latest/download/setup-Happ.x64.exe", FileName = "setup-Happ.x64.exe",
                Publisher = "Flyfrog LLC", Detect = StartPack.Detect.Happ, Template = Templates.Happ },
            new AppSource { Id = "incy", Page = "https://github.com/INCY-DEV/incy-platforms/releases", PageHint = "Скачайте incy-windows-setup.exe (раздел Assets последнего выпуска) и запустите.", Name = "INCY", About = "VPN-клиент", Host = "github.com/INCY-DEV",
                Url = "https://github.com/INCY-DEV/incy-platforms/releases/download/latest/incy-windows-setup.exe", FileName = "incy-windows-setup.exe",
                Sha256From = "sums:https://github.com/INCY-DEV/incy-platforms/releases/download/latest/SHA256SUMS",
                Detect = StartPack.Detect.Incy, Template = Templates.Incy },
            new AppSource { Id = "koala", Page = "https://github.com/coolcoala/koala-clash/releases/latest", PageHint = "Скачайте Koala.Clash_x64-setup.exe (раздел Assets) и запустите.", Name = "Koala Clash", About = "VPN-клиент", Host = "github.com/coolcoala",
                Url = "https://github.com/coolcoala/koala-clash/releases/latest/download/Koala.Clash_x64-setup.exe", FileName = "Koala.Clash_x64-setup.exe",
                Sha256From = "github:coolcoala/koala-clash", Detect = StartPack.Detect.Koala, Template = Templates.Koala },
        };

        public static AppSource Get(string id) { return All.FirstOrDefault(a => a.Id == id); }

        // Запасной путь: открыть официальную страницу загрузки с короткой подсказкой, что там нажать.
        public static void OpenPage(System.Windows.Forms.IWin32Window owner, AppSource a)
        {
            if (a == null || a.Page == null) return;
            if (!Dlg.Ask(owner, "Скачать " + a.Name + " с официального сайта",
                a.PageHint + "\n\nПосле установки вернитесь в Старт-пак — он сам найдёт программу. Если не нашёл: «⋯» вверху → «Найти программы заново».\n\n" + a.Page,
                "Открыть сайт", "Закрыть")) return;
            try { Core.ShellOpen(a.Page); Journal.Write("установка", a.Name + ": открыта страница " + a.Page); }
            catch (Exception ex) { Dlg.Info(owner, "Не открылось", "Скопируйте ссылку в браузер:\n" + a.Page + "\n\n" + ex.Message); }
        }

        // Какой из известных программ соответствует карточка.
        public static string KindOf(Item it)
        {
            if (it.UpdateKind == "zapret") return "zapret";
            if (it.Repo.Equals("Flowseal/tg-ws-proxy", StringComparison.OrdinalIgnoreCase)) return "tgws";
            if (it.Special == "happ") return "happ";
            if (it.Special == "incy") return "incy";
            if (it.Special == "koala") return "koala";
            if (it.Special == "spotify") return "spotify";
            if (it.ProcessName.Equals("Telegram", StringComparison.OrdinalIgnoreCase)) return "telegram";
            if (it.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase)) return "discord";
            return null;
        }

        public static string SafeDetect(AppSource a)
        {
            // только для самопроверки: «притвориться», что программа не установлена
            string pretend = Environment.GetEnvironmentVariable("STARTPACK_TEST_PRETEND_MISSING");
            if (!string.IsNullOrEmpty(pretend) && pretend.Split(',').Contains(a.Id)) return null;
            try { return a.Detect(); } catch { return null; }
        }

        // ---------- цифровая подпись

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WinTrustFileInfo
        {
            public int cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WinTrustData
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData, pwszURLReference;
            public int dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        static readonly Guid VerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // Подпись цела и выдана доверенным центром (без проверки отзыва — она бывает долгой).
        public static bool SignatureValid(string path)
        {
            var fi = new WinTrustFileInfo { cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo)), pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(fi.cbStruct);
            IntPtr pData = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var wd = new WinTrustData
                {
                    cbStruct = Marshal.SizeOf(typeof(WinTrustData)), dwUIChoice = 2 /* без окон */, fdwRevocationChecks = 0,
                    dwUnionChoice = 1 /* файл */, pFile = pFile, dwStateAction = 0, dwProvFlags = 0x10 /* без проверки отзыва */
                };
                pData = Marshal.AllocHGlobal(wd.cbStruct);
                Marshal.StructureToPtr(wd, pData, false);
                return WinVerifyTrust(new IntPtr(-1), VerifyV2, pData) == 0;
            }
            catch { return false; }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(pFile);
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
            }
        }

        public static string SignerName(string path)
        {
            try
            {
                X509Certificate c = X509Certificate.CreateFromSignedFile(path);
                foreach (string part in c.Subject.Split(','))
                {
                    string p = part.Trim();
                    if (p.StartsWith("CN=")) return p.Substring(3).Trim('"');
                }
                return c.Subject;
            }
            catch { return null; }
        }

        // Неподписанный установщик: сверить SHA-256 с опубликованной разработчиком суммой.
        // null — совпало, иначе — почему запускать нельзя.
        public static string CheckSha256(string path, AppSource a)
        {
            string actual;
            using (var fs = File.OpenRead(path))
            using (var h = System.Security.Cryptography.SHA256.Create())
                actual = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            string expected = null;
            try
            {
                if (a.Sha256From.StartsWith("sums:"))
                {
                    using (var wc = Core.NewClient())
                        foreach (string line in wc.DownloadString(a.Sha256From.Substring(5)).Split('\n'))
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                            if (m.Success && m.Groups[2].Value.Trim() == a.FileName) expected = m.Groups[1].Value.ToLowerInvariant();
                        }
                }
                else if (a.Sha256From.StartsWith("github:"))
                {
                    using (var wc = Core.NewClient())
                    {
                        wc.Headers[System.Net.HttpRequestHeader.Accept] = "application/vnd.github+json";
                        string json = wc.DownloadString("https://api.github.com/repos/" + a.Sha256From.Substring(7) + "/releases/latest");
                        var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                        foreach (object o in (System.Collections.IEnumerable)d["assets"])
                        {
                            var asset = o as Dictionary<string, object>;
                            if (asset == null || Convert.ToString(asset["name"]) != a.FileName || !asset.ContainsKey("digest")) continue;
                            string dg = Convert.ToString(asset["digest"]);
                            if (dg.StartsWith("sha256:")) expected = dg.Substring(7).ToLowerInvariant();
                        }
                    }
                }
            }
            catch (Exception ex) { return "не удалось получить контрольную сумму с сайта разработчика: " + ex.Message; }
            if (expected == null) return "разработчик не опубликовал контрольную сумму для " + a.FileName;
            if (expected != actual) return "контрольная сумма не совпадает с опубликованной разработчиком — файл повреждён или подменён";
            return null;
        }

        // Проверка установщика: подпись издателя или контрольная сумма.
        public static string Verify(string path, AppSource a)
        {
            return a.Publisher != null ? CheckSigned(path, a.Publisher) : a.Sha256From != null ? CheckSha256(path, a) : "нечем проверить файл";
        }

        // null — всё в порядке, иначе — почему запускать нельзя.
        public static string CheckSigned(string path, string publisher)
        {
            string who = SignerName(path);
            if (who == null) return "у файла нет цифровой подписи";
            if (!SignatureValid(path)) return "цифровая подпись файла недействительна";
            if (who.IndexOf(publisher, StringComparison.OrdinalIgnoreCase) < 0) return "файл подписан «" + who + "», а должен — «" + publisher + "»";
            return null;
        }
    }

    // ---------------------------------------------------------------- окно «Установить программы»

    public class InstallsForm : ThemedForm
    {
        public string Chosen;        // id программы, которую нажали
        public string ChosenAction;  // "install" или "add"

        public InstallsForm(Config cfg)
        {
            Text = "Установить программы";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(26), S(18), S(26), S(22)) };
            Controls.Add(t);
            Label head = Lbl("Установить программы", "title");
            head.Margin = new Padding(0, 0, 0, S(4));
            t.Controls.Add(head);
            Label sub = Lbl("«Установить» — Старт-пак сам скачает программу с официального сайта, проверит подпись и запустит установку.\n" +
                "«С сайта» — откроет официальную страницу: скачайте и установите как обычно. Старт-пак потом сам найдёт программу.", "dim");
            sub.MaximumSize = new Size(S(560), 0);
            sub.Margin = new Padding(0, 0, 0, S(14));
            t.Controls.Add(sub);

            foreach (AppSource a in Installers.All)
            {
                string found = Installers.SafeDetect(a);
                bool inList = cfg.Items.Any(i => Installers.KindOf(i) == a.Id && Core.PathExists(i.Path));
                var row = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, BackColor = Theme.Current.Surface, Tag = "surface", Padding = new Padding(S(14), S(10), S(12), S(10)), Margin = new Padding(0, 0, 0, S(8)) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(300)));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(120)));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(130)));   // одинаковая ширина у всех строк
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(110)));

                var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Current.Surface, Tag = "surface" };
                Label name = Lbl(a.Name, "");
                name.Font = Fonts.Semi(11f);
                Label about = Lbl(a.About + " · " + a.Host, "dim");
                text.Controls.Add(name);
                text.Controls.Add(about);
                row.Controls.Add(text, 0, 0);

                Label state = Lbl(found == null ? "не установлена" : inList ? "есть в списке" : "установлена", "");
                state.ForeColor = found == null ? Theme.Current.TextDim : Theme.Current.Green;
                state.Font = Fonts.Semi(9.5f);
                state.Margin = new Padding(0, S(12), 0, 0);
                row.Controls.Add(state, 1, 0);

                AppSource src = a;
                if (found == null)
                {
                    var b = new FlatBtn("Установить", BtnKind.Primary, Glyph.Download) { Compact = true, Margin = new Padding(0, S(4), 0, 0) };
                    b.Fit();
                    b.Click += delegate { Chosen = src.Id; ChosenAction = "install"; DialogResult = DialogResult.OK; Close(); };
                    row.Controls.Add(b, 2, 0);
                }
                else if (!inList)
                {
                    var b = new FlatBtn("В список", BtnKind.Secondary, Glyph.Add) { Compact = true, Margin = new Padding(0, S(4), 0, 0) };
                    b.Fit();
                    b.Click += delegate { Chosen = src.Id; ChosenAction = "add"; DialogResult = DialogResult.OK; Close(); };
                    row.Controls.Add(b, 2, 0);
                }
                if (a.Page != null)
                {
                    var site = new FlatBtn("С сайта", BtnKind.Ghost, Glyph.Web) { Compact = true, Margin = new Padding(S(6), S(4), 0, 0) };
                    site.Fit();
                    site.Click += delegate { Installers.OpenPage(this, src); };
                    new ToolTip().SetToolTip(site, "Открыть официальную страницу загрузки " + src.Name);
                    row.Controls.Add(site, 3, 0);
                }
                t.Controls.Add(row);
            }

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(10), 0, 0) };
            var close = new FlatBtn("Закрыть", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
            close.Fit();
            buttons.Controls.Add(close);
            t.Controls.Add(buttons);
            CancelButton = close;
        }
    }
}
