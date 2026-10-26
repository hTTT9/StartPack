// Установка и удаление самого Старт-пака.
// StartPack.exe — сам себе установщик: если его запустили не из папки установки (из архива, из «Загрузок»),
// он предлагает поставить себя в %LOCALAPPDATA%\Programs\StartPack (без прав администратора),
// с ярлыками на рабочем столе и в «Пуске» и строкой в «Приложениях» Windows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StartPack
{
    public static class Setup
    {
        public const string ExitEvent = @"Local\StartPack-exit";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StartPack";
        const string Desc = "Запустить zapret, прокси, VPN, Telegram и Discord одним кликом";

        public static string InstallDir { get { return Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs", "StartPack"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "StartPack.exe"); } }
        // Самопроверка: ярлыки — в свою папку, реестр и чужие ярлыки не трогаются, ничего не запускается.
        static readonly string TestLnkDir = Environment.GetEnvironmentVariable("STARTPACK_TEST_LNKDIR");
        public static bool TestMode { get { return !string.IsNullOrEmpty(TestLnkDir); } }

        public static string DesktopLnk { get { return Path.Combine(TestMode ? TestLnkDir : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Старт-пак.lnk"); } }
        public static string StartMenuLnk { get { return Path.Combine(TestMode ? Path.Combine(TestLnkDir, "Пуск") : Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Старт-пак.lnk"); } }

        public static bool IsInstalled { get { return ZapretCtl.SameDir(Core.AppDir, InstallDir); } }

        // Запуск прямо из архива (Проводник распаковывает его во временную папку) — список там не сохранится.
        public static bool FromTemp
        {
            get
            {
                string d = Core.AppDir.TrimEnd('\\') + "\\";
                string tmp = Path.GetTempPath();
                return d.StartsWith(tmp, StringComparison.OrdinalIgnoreCase) || d.IndexOf(".zip\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       d.IndexOf("\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static string InstalledVersion()
        {
            try
            {
                if (!File.Exists(InstalledExe)) return null;
                string v = Core.NormalizeVer(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion);
                while (v != null && v.Count(c => c == '.') > 1 && v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
                return v;
            }
            catch { return null; }
        }

        // Вызывается из Main до всего остального. true — этому процессу пора выйти.
        public static bool OfferInstall()
        {
            if (IsInstalled) { Register(); return false; }
            Theme.Apply(new Config().Theme);
            if (TestMode)
            {
                Install(true);
                Journal.Write("установка", "(проверка) установлено, запуск пропущен");
                return true;
            }
            using (var f = new SetupForm())
            {
                f.ShowDialog();
                if (f.Choice != "install") return true;
                try
                {
                    Install(f.Desktop);
                    Process.Start(new ProcessStartInfo(InstalledExe) { WorkingDirectory = InstallDir });
                }
                catch (Exception ex)
                {
                    Dlg.Info(null, "Не получилось установить", ex.Message);
                }
                return true;
            }
        }

        // Скопировать себя в папку установки, сделать ярлыки и строку в «Приложениях».
        public static void Install(bool desktopLnk)
        {
            string dest = InstallDir;
            Directory.CreateDirectory(dest);
            // переносные копии: работающие сейчас и та, из которой запущена установка
            var portables = CloseInstalled();
            if (!FromTemp && !IsInstalled) portables.Add(Core.AppDir);

            string exe = InstalledExe;
            string tmp = exe + ".new";
            // байты, а не копия файла: так же, как это делают обычные установщики
            File.WriteAllBytes(tmp, File.ReadAllBytes(Application.ExecutablePath));
            for (int i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(exe)) File.Delete(exe);
                    File.Move(tmp, exe);
                    break;
                }
                catch (IOException)
                {
                    if (i >= 10) throw new IOException("старый StartPack.exe занят — закройте Старт-пак (значок у часов → Выйти) и повторите");
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException)
                {
                    if (i >= 10) throw;
                    Thread.Sleep(500);
                }
            }

            AdoptPortable(portables.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), dest);

            string icon = exe + ",0";
            try
            {
                string ico = Directory.GetFiles(dest, "StartPack-*.ico").OrderByDescending(x => x).FirstOrDefault();
                if (ico != null) icon = ico + ",0";
            }
            catch { }
            Directory.CreateDirectory(Path.GetDirectoryName(StartMenuLnk));
            Core.CreateShortcut(StartMenuLnk, exe, "", Desc, icon);
            if (desktopLnk) Core.CreateShortcut(DesktopLnk, exe, "", Desc, icon);
            if (TestMode) return;
            // уже сделанные из настроек ярлыки — на новое место
            if (File.Exists(Core.StartupLnk)) Core.CreateShortcut(Core.StartupLnk, exe, "/autorun", "Старт-пак: запустить всё", icon);
            if (File.Exists(Core.RunAllLnk)) Core.CreateShortcut(Core.RunAllLnk, exe, "/autorun", "Запустить всё из Старт-пака одним двойным щелчком", icon);
            Register(exe);
            Journal.Write("установка", "Старт-пак " + Core.Version + " установлен в " + dest);
        }

        // Закрыть работающий Старт-пак (любую копию: одновременно может работать только один),
        // чтобы заменить файл и чтобы новая версия не уступила место старой. Возвращает папки переносных копий.
        static List<string> CloseInstalled()
        {
            var portable = new List<string>();
            int me = Process.GetCurrentProcess().Id;
            var procs = Process.GetProcessesByName("StartPack").Where(p => p.Id != me).ToList();
            foreach (Process p in procs)
            {
                string dir = null;
                try { dir = Core.SafeDir(Core.ProcessPath(p.Id)); } catch { }
                if (dir != null && !ZapretCtl.SameDir(dir, InstallDir)) portable.Add(dir);
            }
            if (TestMode) return new List<string>();   // самопроверка не закрывает настоящий Старт-пак
            if (procs.Count == 0) return portable;
            try { using (var e = EventWaitHandle.OpenExisting(ExitEvent)) e.Set(); } catch { }
            foreach (Process p in procs)
            {
                try { if (!p.WaitForExit(8000)) { p.Kill(); p.WaitForExit(4000); } } catch { }
                p.Dispose();
            }
            return portable;
        }

        // Перенести переносную копию в установленную: самый свежий список программ (старый — в .bak)
        // и скачанное в её папку apps (TG WS Proxy); пути в списке — на новое место.
        static void AdoptPortable(List<string> dirs, string dest)
        {
            string cfgThere = Path.Combine(dest, "StartPack.json");
            DateTime best = File.Exists(cfgThere) ? File.GetLastWriteTimeUtc(cfgThere) : DateTime.MinValue;
            string from = null;
            foreach (string d in dirs)
            {
                string c = Path.Combine(d, "StartPack.json");
                if (File.Exists(c) && File.GetLastWriteTimeUtc(c) > best) { best = File.GetLastWriteTimeUtc(c); from = d; }
            }
            foreach (string d in dirs)
            {
                string apps = Path.Combine(d, "apps");
                if (!Directory.Exists(apps)) continue;
                try
                {
                    Directory.CreateDirectory(Path.Combine(dest, "apps"));
                    foreach (string f in Directory.GetFiles(apps))
                    {
                        string to = Path.Combine(dest, "apps", Path.GetFileName(f));
                        if (!File.Exists(to)) File.Copy(f, to);
                    }
                }
                catch { }
            }
            if (from == null) return;
            try
            {
                if (File.Exists(cfgThere)) File.Copy(cfgThere, cfgThere + ".bak", true);
                string json = File.ReadAllText(Path.Combine(from, "StartPack.json"), System.Text.Encoding.UTF8);
                // пути к скачанному (apps\…) — в папку установки
                string a = (from.TrimEnd('\\') + @"\apps\").Replace(@"\", @"\\");
                string b = (dest.TrimEnd('\\') + @"\apps\").Replace(@"\", @"\\");
                json = json.Replace(a, b);
                File.WriteAllText(cfgThere, json, new System.Text.UTF8Encoding(false));
                Journal.Write("установка", "список программ перенесён из " + from);
            }
            catch (Exception ex) { Journal.Write("установка", "список не перенёсся: " + ex.Message); }
        }

        // Строка «Старт-пак» в «Параметры → Приложения» (там же — кнопка «Удалить»).
        public static void Register(string exe = null)
        {
            if (TestMode) return;
            exe = exe ?? Application.ExecutablePath;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Старт-пак");
                    k.SetValue("DisplayVersion", Core.Version);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", Path.GetDirectoryName(exe));
                    k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long kb = 0;
                    try { kb = new FileInfo(exe).Length / 1024; } catch { }
                    k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // Удалить Старт-пак: ярлыки, строку в «Приложениях», папку программы (после выхода).
        // Скачанное Старт-паком (папка apps — например, TG WS Proxy) остаётся, если там что-то есть.
        public static string Uninstall()
        {
            foreach (string l in TestMode ? new[] { DesktopLnk, StartMenuLnk } : new[] { DesktopLnk, StartMenuLnk, Core.StartupLnk, Core.RunAllLnk })
            {
                try
                {
                    if (File.Exists(l) && ZapretCtl.SameDir(Core.SafeDir(Core.ResolveLnk(l)), Core.AppDir)) File.Delete(l);
                }
                catch { }
            }
            if (!TestMode) try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            string dir = Core.AppDir;
            string apps = Templates.AppsDir;
            bool keepApps = Directory.Exists(apps) && Directory.EnumerateFileSystemEntries(apps).Any();
            var cmds = new List<string> { "timeout /t 2 /nobreak >nul" };
            foreach (string d in Directory.GetDirectories(dir))
                if (!(keepApps && ZapretCtl.SameDir(d, apps))) cmds.Add("rmdir /s /q \"" + d + "\"");
            foreach (string f in Directory.GetFiles(dir))
                cmds.Add("del /f /q \"" + f + "\"");
            if (!keepApps) cmds.Add("cd /d \"%TEMP%\" & rmdir /q \"" + dir + "\"");
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + string.Join(" & ", cmds) + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(psi);
            return keepApps ? apps : null;
        }

        // /update: новая версия, скачанная Старт-паком, ставит себя без вопросов и открывается.
        public static void SilentUpdate()
        {
            try
            {
                bool desk = !File.Exists(InstalledExe) || File.Exists(DesktopLnk);   // ярлык на столе — как было
                Install(desk);
                if (TestMode) { Journal.Write("установка", "(проверка) обновление поставлено, запуск пропущен"); return; }
                Process.Start(new ProcessStartInfo(InstalledExe, "/updated") { WorkingDirectory = InstallDir });
            }
            catch (Exception ex)
            {
                Theme.Apply(new Config().Theme);
                Dlg.Info(null, "Не получилось обновить Старт-пак", ex.Message + "\n\nНовую версию можно скачать вручную: " + SelfUpdate.ReleasesPage);
            }
        }

        // /uninstall из «Приложений» Windows.
        public static void UninstallInteractive()
        {
            Theme.Apply(new Config().Theme);
            if (TestMode) { CloseInstalled(); Uninstall(); return; }
            if (!Dlg.Ask(null, "Удалить Старт-пак?", "Удалятся программа, её ярлыки и список программ. Сами программы — zapret, Discord, VPN и другие — останутся.", "Удалить", "Отмена")) return;
            CloseInstalled();
            string kept = Uninstall();
            Dlg.Info(null, "Старт-пак удалён", kept != null ? "Скачанное Старт-паком (например, TG WS Proxy) осталось в папке:\n" + kept : "Готово.");
        }
    }

    public class SetupForm : ThemedForm
    {
        public string Choice;
        readonly Toggle desk;
        public bool Desktop { get { return desk.Checked; } }

        public SetupForm()
        {
            Text = "Старт-пак " + Core.Version;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(28), S(22), S(28), S(22)) };
            Controls.Add(t);

            var top = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, S(12)) };
            try
            {
                using (Stream s = typeof(SetupForm).Assembly.GetManifestResourceStream("StartPack.app-256.png"))
                    if (s != null)
                        top.Controls.Add(new PictureBox { Image = new Bitmap(Image.FromStream(s), S(44), S(44)), Size = new Size(S(44), S(44)), Margin = new Padding(0, 0, S(14), 0), SizeMode = PictureBoxSizeMode.Zoom });
            }
            catch { }
            var heads = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            Label head = Lbl("Старт-пак", "title");
            head.Font = Fonts.Semi(16f);
            head.Margin = new Padding(0, 0, 0, S(2));
            heads.Controls.Add(head);
            heads.Controls.Add(Lbl("zapret, прокси, VPN, Telegram и Discord — одной кнопкой", "dim"));
            top.Controls.Add(heads);
            t.Controls.Add(top);

            string have = Setup.InstalledVersion();
            var points = new List<string>
            {
                "Установка за пару секунд, права администратора не нужны",
                "Ярлык на рабочем столе и в меню «Пуск»",
                "Удалить можно в любой момент: «Параметры» → «Приложения»"
            };
            if (have != null)
                points.Insert(0, Core.CompareVersions(have, Core.Version) > 0
                    ? "Сейчас установлена более новая версия " + have + " — она заменится этой"
                    : "Уже установлена версия " + have + " — она обновится, ваш список программ сохранится");
            foreach (string pt in points)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, S(6)) };
                Label mark = Lbl(Glyph.Check, "");
                mark.Font = Fonts.Icons(10f);
                mark.ForeColor = Theme.Current.Green;
                mark.Margin = new Padding(0, S(3), S(8), 0);
                Label txt = Lbl(pt, "");
                txt.MaximumSize = new Size(S(440), 0);
                row.Controls.Add(mark);
                row.Controls.Add(txt);
                t.Controls.Add(row);
            }

            desk = new Toggle("Ярлык на рабочем столе", null, true) { Margin = new Padding(0, S(10), 0, S(6)) };
            desk.Fit();
            t.Controls.Add(desk);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(18), 0, 0) };
            var ok = new FlatBtn(have != null ? "Обновить и открыть" : "Установить и открыть", BtnKind.Primary, Glyph.Download) { Font = Fonts.Semi(10.5f) };
            ok.Fit();
            ok.Click += delegate { Choice = "install"; Close(); };
            buttons.Controls.Add(ok);
            var cancel = new FlatBtn("Отмена", BtnKind.Secondary, null) { Margin = new Padding(0, 3, S(8), 3) };
            cancel.Fit();
            cancel.Click += delegate { Choice = null; Close(); };
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (AcceptButton is Control) ((Control)AcceptButton).Focus();
        }
    }
}
