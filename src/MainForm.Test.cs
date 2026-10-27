// Самопроверка (/test <лог>) — только для разработки. Окна снимаются на втором мониторе.
// Ничего не меняет в Happ и не просит прав администратора.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm
    {
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        static bool Env(string name) { return Environment.GetEnvironmentVariable(name) == "1"; }

        async Task RunTest()
        {
            Log("=== тест " + Core.Version + ": тема " + Theme.Mode + ", элементов " + cfg.Items.Count + ", профиль «" + cfg.ActiveProfile + "», профилей " + cfg.Profiles.Count + ", окно на экране " + Screen.FromControl(this).DeviceName);
            foreach (Item it in cfg.Items)
                Log("  " + it.Name + " | " + it.Path + " | процесс " + it.ProcessName + " | тип " + (it.Special.Length > 0 ? it.Special : "-") +
                    " | сторож " + it.Watch + " | ждать " + it.WaitReady + " | в профиле " + it.Enabled + " | свой автозапуск " + (AutoStartCtl.FindEntry(it) ?? "-"));
            Log("починка путей при загрузке: " + (Core.LastRepairs.Count > 0 ? string.Join(", ", Core.LastRepairs) : "не понадобилась"));
            foreach (Item it in cfg.Items) Log("  после починки: " + it.Name + " → " + it.Path + (Core.PathExists(it.Path) ? "" : "  [НЕ НАЙДЕН]") + (it.Special == "spotify" ? " | hostsfix " + it.HostsFixDir : ""));
            RefreshVpn();
            Log("VPN: Happ подключён " + vpn.Happ + ", INCY подключён " + vpn.Incy + ", Koala открыта " + vpn.KoalaRunning + ", сервер Happ " + (happServerName ?? "-"));
            HappPreset cap = HappCtl.Capture();
            if (cap != null)
            {
                string det;
                Log("Happ: запомнился «" + cap.Name + "», совпадает с реестром: " + HappCtl.RoundTripOk(cap, out det));
            }
            Log("трей: " + (tray != null ? "значок есть, состояние " + trayState + ", подсказка «" + tray.Text + "»" : "нет"));
            foreach (Item z in cfg.Items.Where(i => i.UpdateKind == "zapret"))
                Log("zapret: стратегия " + ZapretCtl.Pretty(ZapretCtl.Current(z)) + ", всего стратегий " + ZapretCtl.Strategies(z).Count + ", тест есть: " + System.IO.File.Exists(ZapretCtl.TestScript(z) ?? ""));
            Log("уборка нашла: " + string.Join(" | ", Cleanup.Scan(cfg).Select(c => c.Title + " — " + c.Hint)));
            var swz = Stopwatch.StartNew();
            Detect.ForgetZapret();
            var allZ = Detect.ZapretAll();
            Log("zapret на ПК (" + swz.ElapsedMilliseconds + " мс): " + string.Join(" | ", allZ.Select(z => z.Root + " = " + z.Version)));
            Log("самый новый: " + (Detect.Zapret() ?? "-"));
            Log("службы zapret: " + string.Join(" | ", ZapretCtl.Services().Select(s => s.Name + " → " + s.Root)));
            foreach (Item z in cfg.Items.Where(i => i.UpdateKind == "zapret"))
                Log("zapret в списке: " + ZapretCtl.Dir(z) + ", чужие службы: " + ZapretCtl.Describe(ZapretCtl.Foreign(ZapretCtl.Dir(z))));
            Log("установлен: " + Setup.IsInstalled + ", из временной папки: " + Setup.FromTemp + ", установленная версия: " + (Setup.InstalledVersion() ?? "-"));

            if (Env("STARTPACK_TEST_UI37"))
            {
                await CheckNet(true);
                Log("связь: " + string.Join(", ", NetSummary()));
                // перенос карточки стрелкой: вторая карточка — вверх
                if (cfg.Items.Count >= 2)
                {
                    string before = string.Join(" / ", cfg.Items.Select(i => i.Name));
                    cards.Focus();
                    cards.Selected = cfg.Items[1];
                    PostMessage(cards.Handle, 0x100 /* WM_KEYDOWN */, (IntPtr)Keys.Up, IntPtr.Zero);
                    for (int k = 0; k < 5; k++) { Application.DoEvents(); Thread.Sleep(50); }
                    Log("стрелка вверх: было «" + before + "», стало «" + string.Join(" / ", cfg.Items.Select(i => i.Name)) + "»");
                    PostMessage(cards.Handle, 0x100, (IntPtr)Keys.Down, IntPtr.Zero);
                    for (int k = 0; k < 5; k++) { Application.DoEvents(); Thread.Sleep(50); }
                    Log("стрелка вниз: вернулось = " + (string.Join(" / ", cfg.Items.Select(i => i.Name)) == before));
                    Snap(this, testLog + ".selected.png");
                    cards.Selected = null;
                }
                // подсказка «Запуск с Windows»
                MaybeShowAutostartTip(true);
                SnapScreen(testLog + ".tip.png");
                Log("подсказка: показана = " + (autoTip != null) + ", подсветка = " + autoToggle.Glow + ", кнопка пинга видна = " + btnPing.Visible);
                HideAutostartTip();
                Log("подсказка после «Не сейчас»: есть = " + (autoTip != null) + ", отмечена показанной = " + cfg.TipAutostartShown);

                // ■ «Остановить»: тихий ping как тестовая программа
                var psi = new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false };
                Process pp = Process.Start(psi);
                var dummy = new Item { Name = "Тест-пинг", Path = @"C:\Windows\System32\PING.EXE", ProcessName = "PING" };
                cfg.Items.Insert(0, dummy);
                RefreshState();
                Log("тест-пинг: работает = " + Describe(dummy).Running + ", подсказка кнопки = «" + (Describe(dummy).Running ? "Остановить" : "Запустить") + "»");
                Snap(this, testLog + ".stop.png");
                await StopItem(dummy);
                Log("тест-пинг после ■: процесс закрыт = " + pp.HasExited + ", статус «" + Describe(dummy).Status + "»");
                cfg.Items.Remove(dummy);
                SyncCards();

                // оверлей: обычный и крупный «все программы» без состояния
                cfg.Overlay = true;
                Screen scr = Screen.AllScreens.FirstOrDefault(x => !x.Primary) ?? Screen.PrimaryScreen;
                cfg.OverlayX = scr.WorkingArea.Right - S(420); cfg.OverlayY = scr.WorkingArea.Top + S(40);
                cfg.OverlayClickThrough = false;
                ApplyOverlay();
                Snap(overlay, testLog + ".overlay.png");
                Log("оверлей: " + overlay.Width + "×" + overlay.Height);
                cfg.OverlayScale = 120; cfg.OverlayShow = "all"; cfg.OverlayStatus = false; cfg.OverlayOpacity = 100;
                ApplyOverlay();
                Snap(overlay, testLog + ".overlay-big.png");
                Log("оверлей крупный, все программы: " + overlay.Width + "×" + overlay.Height);
                cfg.OverlayScale = 85; cfg.OverlayShow = "running"; cfg.OverlayStatus = true; cfg.OverlayButtons = false; cfg.OverlayMs = false;
                ApplyOverlay();
                Snap(overlay, testLog + ".overlay-small.png");
                Log("оверлей мелкий, только запущенные, без кнопок: " + overlay.Width + "×" + overlay.Height);
                cfg.Overlay = false;
                ApplyOverlay();
                using (var f = new OverlaySettingsForm(cfg, null)) SnapDialog(f, testLog + ".overlay-settings.png");

                // экспорт → импорт
                string exp = System.IO.Path.Combine(Core.AppDir, "export-test.json");
                Core.ExportTo(cfg, exp);
                Config back = Core.FromFile(exp);
                Log("экспорт/импорт: программ " + cfg.Items.Count + " → " + back.Items.Count + ", совпадают имена = " +
                    cfg.Items.Select(i => i.Name).SequenceEqual(back.Items.Select(i => i.Name)) + ", тема " + back.Theme + ", оверлей-размер " + back.OverlayScale);
                string junk = System.IO.Path.Combine(Core.AppDir, "junk.json");
                System.IO.File.WriteAllText(junk, "{\"a\": 1}");
                try { Core.FromFile(junk); Log("импорт мусора: принят?!"); } catch (Exception ex) { Log("импорт мусора: отказ — " + ex.Message); }
                HandleSettingsRequest("import", exp);
                Log("после импорта: программ " + cfg.Items.Count + ", .bak есть = " + System.IO.File.Exists(Core.ConfigPath + ".bak"));
                cfg.Theme = "light"; cfg.Hotkey = false;
                HandleSettingsRequest("reset", null);
                Log("после сброса настроек: тема " + cfg.Theme + ", горячая клавиша " + cfg.Hotkey + ", программ " + cfg.Items.Count);
                HandleSettingsRequest("resetall", null);
                Log("после сброса всего: программ " + cfg.Items.Count + " (" + string.Join(", ", cfg.Items.Select(i => i.Name + (i.Enabled ? "" : " [выкл]"))) + ")");
                Snap(this, testLog + ".after-reset.png");
            }

            if (Env("STARTPACK_TEST_DEMO"))
            {
                // чистый список для снимка в README: VPN — как «не установлены», без названий серверов
                demo = true;
                cfg.Items.Clear();
                AddStandardSet();
                foreach (Item v in cfg.Items.Where(VpnCtl.IsVpn)) { v.Path = ""; v.ProcessName = "startpack-demo-none"; }
                RefreshState();
                happServerName = null; happGuid = null;
                await CheckUpdates(false);
                Status("Нажмите «Запустить всё» или запускайте программы по одной.");
                SyncCards();
                RefreshState();
                SnapFrame(this, testLog + ".demo.png");
            }

            if (Env("STARTPACK_TEST_SELFUPDATE"))
            {
                SelfUpdate.Info si = null;
                try { si = await SelfUpdate.Check(); } catch (Exception ex) { Log("самообновление: ошибка проверки — " + ex.Message); }
                Log("самообновление: " + (si == null ? "выпусков нет (" + SelfUpdate.LastError + ")" : "выпуск " + si.Version + ", новее = " + si.Newer + ", exe " + si.ExeUrl + ", sha256 " + (si.Sha256 ?? "-") + ", заметки «" + si.Notes + "»"));
                RefreshState();
                Log("кнопка «Обновить» вверху: видна = " + btnUpdate.Visible + ", «" + btnUpdate.Text + "»");
                Snap(this, testLog + ".selfupdate.png");
                await RunSelfUpdate();
                using (var f = new SettingsForm(cfg, null, null)) SnapDialog(f, testLog + ".settings-update.png");
                // проверка файла: правильная сумма и версия — можно ставить
                string me = Application.ExecutablePath;
                string hash;
                using (var fs = System.IO.File.OpenRead(me)) using (var h = System.Security.Cryptography.SHA256.Create())
                    hash = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                Log("проверка своего exe как выпуска " + Core.Version + ": " + (SelfUpdate.Verify(me, new SelfUpdate.Info { Version = Core.Version, Sha256 = hash }) ?? "годится"));
                Log("с неверной суммой: " + SelfUpdate.Verify(me, new SelfUpdate.Info { Version = Core.Version, Sha256 = new string('0', 64) }));
                Log("с чужой версией: " + SelfUpdate.Verify(me, new SelfUpdate.Info { Version = "9.9", Sha256 = hash }));
            }

            if (Env("STARTPACK_TEST_KINDS"))
            {
                // «испорченные» карточки: тип и обновления не от той программы
                string happ = Detect.Happ() ?? @"C:\Program Files\Happ\Happ.exe";
                var c = new Config();
                c.Items.Add(new Item { Name = "Happ как Koala", Path = happ, ProcessName = "Happ", Special = "koala", UpdateKind = "zapret", Repo = "Flowseal/zapret-discord-youtube" });
                c.Items.Add(new Item { Name = "Блокнот с обновлениями zapret", Path = @"C:\Windows\notepad.exe", ProcessName = "notepad", Special = "happ", UpdateKind = "zapret", Repo = "Flowseal/zapret-discord-youtube" });
                c.Items.Add(new Item { Name = "Своя с GitHub", Path = @"C:\Windows\notepad.exe", ProcessName = "notepad", UpdateKind = "github-exe", Repo = "someone/tool", Asset = "tool.exe" });
                Item z = cfg.Items.FirstOrDefault(i => i.UpdateKind == "zapret");
                if (z != null) c.Items.Add(new Item { Name = "zapret без обновлений", Path = z.Path, ProcessName = "", Special = "incy" });
                string warp = Detect.Warp();
                if (warp != null) c.Items.Add(new Item { Name = "WARP без типа", Path = warp, ProcessName = "x" });
                foreach (Item i in c.Items) i.Normalize();
                Core.Repair(c);
                foreach (Item i in c.Items)
                    Log("тип: «" + i.Name + "» → особый «" + i.Special + "», обновления «" + i.UpdateKind + "», репозиторий «" + i.Repo + "», процесс «" + i.ProcessName + "»");
                // окно «Изменить» для Happ: тип и обновления заблокированы
                Item hi = c.Items[0];
                using (var f = new ItemForm(hi, "Изменить: Happ")) SnapDialog(f, testLog + ".edit-kind-happ.png");
                using (var f = new ItemForm(c.Items[1], "Изменить: своя")) SnapDialog(f, testLog + ".edit-kind-own.png");

                // INCY
                string incy = Detect.Incy();
                Log("INCY найден: " + (incy ?? "нет") + ", настройки: " + (IncyCtl.PrefsFile(null) ?? "нет") + ", подключён сейчас: " + IncyCtl.Connected(null, Core.RunningNames(), false));
                string pf = IncyCtl.PrefsFile(null);
                if (pf != null)
                {
                    string copy = System.IO.Path.Combine(Core.AppDir, "incy-prefs-copy.json");
                    System.IO.File.Copy(pf, copy, true);
                    bool changed = IncyCtl.PresetFile(copy, true);
                    bool again = IncyCtl.PresetFile(copy, true);
                    Log("INCY автоподключение (на копии настроек): включено = " + changed + ", повторно ничего не меняет = " + !again +
                        ", в файле теперь: " + System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(copy), "\"autoConnect\"\\s*:\\s*\\w+").Value);
                }
            }

            if (Env("STARTPACK_TEST_QUALITY"))
            {
                Item z = cfg.Items.FirstOrDefault(i => i.UpdateKind == "zapret");
                if (z != null)
                {
                    // результаты теста zapret — в его формате (как пишет utils\test zapret.ps1)
                    var lines = new[]
                    {
                        "Config: general (ALT).bat (Type: standard)",
                        "  Discord Main : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 43 ms",
                        "  Discord Gateway : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 45 ms",
                        "  YouTube Web : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 51 ms",
                        "  YouTube Image : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 49 ms",
                        "",
                        "Config: general (ALT2).bat (Type: standard)",
                        "  Discord Main : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 88 ms",
                        "  YouTube Web : HTTP:OK TLS1.2:OK TLS1.3:OK | Ping: 120 ms",
                        "",
                        "Config: general (ALT3).bat (Type: standard)",
                        "  Discord Main : HTTP:OK TLS1.2:ERROR TLS1.3:OK | Ping: 60 ms",
                        "  YouTube Web : HTTP:ERROR TLS1.2:SSL TLS1.3:ERROR | Ping: Timeout",
                        "",
                        "Config: general (FAKE TLS AUTO).bat (Type: standard)",
                        "  Discord Main : HTTP:ERROR TLS1.2:ERROR TLS1.3:ERROR | Ping: Timeout",
                        "  YouTube Web : HTTP:ERROR TLS1.2:ERROR TLS1.3:ERROR | Ping: Timeout",
                        "",
                        "=== ANALYTICS ===",
                        "general (ALT).bat : HTTP OK:  12, ERR:   0, UNSUP:   0, Ping OK:   4, Fail:   0"
                    };
                    var stats = StrategyQuality.ParseTest(lines, DateTime.Now.AddMinutes(-30));
                    foreach (StrategyStat s in stats)
                    {
                        StrategyQuality.Put(z, s);
                        Log("замер: " + s.Bat + " — YouTube " + (s.YtOk ? s.YtMs + " мс" : "нет") + ", Discord " + (s.DcOk ? s.DcMs + " мс" : "нет") + ", успешно " + s.Score + "% → " + StrategyQuality.Label(StrategyQuality.Of(z, s.Bat)));
                    }
                    Log("не проверялась: general.bat → " + StrategyQuality.Label(StrategyQuality.Of(z, "general.bat")));
                    Log("подсказка ALT: " + StrategyQuality.Tip(z, "general (ALT).bat").Replace("\n", " | "));
                    // меню стратегий
                    var m = Menus.New();
                    m.ShowImageMargin = true; m.ShowCheckMargin = true;
                    FillStrategies(m.Items, z);
                    ThemeSubmenus(m.Items);
                    lastMenu = m;
                    m.Show(cards, new Point(S(300), S(20)));
                    SnapScreen(testLog + ".strategies.png");
                    Log("меню: " + string.Join(" / ", m.Items.Cast<ToolStripItem>().Take(6).Select(x => x.Text + " [" + ((ToolStripMenuItem)x).ShortcutKeyDisplayString + "]")));
                    m.Close();
                }
            }

            if (Env("STARTPACK_TEST_RECYCLE"))
            {
                // только тестовая папка рядом с exe
                string d = System.IO.Path.Combine(Core.AppDir, "recycle-test");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d, "sub"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(d, "sub", "a.txt"), "x");
                using (var fs = new System.IO.FileStream(System.IO.Path.Combine(d, "sub", "a.txt"), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                {
                    try { Cleanup.Recycle(d); Log("корзина: занятая папка убралась?! есть = " + System.IO.Directory.Exists(d)); }
                    catch (Exception ex) { Log("корзина, файл занят: " + ex.GetType().Name + ": " + ex.Message + "; папка на месте = " + System.IO.Directory.Exists(d)); }
                }
                try { Cleanup.Recycle(d); Log("корзина, файл свободен: убрана = " + !System.IO.Directory.Exists(d)); }
                catch (Exception ex) { Log("корзина, файл свободен: ошибка " + ex.Message); }
                foreach (var z in Detect.ZapretAll().Skip(1))
                    Log("кто держит " + z.Root + ": " + (Cleanup.Holders(z.Root) ?? "никто"));
                foreach (Item z in cfg.Items.Where(i => i.UpdateKind == "zapret"))
                    Log("команда снятия: " + ZapretCtl.ResetCmd(ZapretCtl.Foreign(ZapretCtl.Dir(z))));
            }

            if (Env("STARTPACK_TEST_STANDARD"))
            {
                cfg.Items.Clear();
                SyncCards();
                Snap(this, testLog + ".empty.png");
                AddStandardSet();
                Log("стандартный набор: " + lblStatus.Text);
                foreach (Item it in cfg.Items)
                    Log("  " + it.Name + " | " + (it.Path.Length > 0 ? it.Path : "(нет)") + " | включён " + it.Enabled + " | статус «" + Describe(it).Status + "» | кнопки " + string.Join(", ", Describe(it).Chips.Select(c => c.Value)));
                Snap(this, testLog + ".standard.png");
                AddStandardSet();
                Log("повторно: " + lblStatus.Text);
            }

            // Ctrl+Alt+S: можно ли занять (в самопроверке сразу отпускаем)
            bool hk = RegisterHotKey(Handle, HotkeyId + 1, 0x2 | 0x1 | 0x4000, (uint)Keys.S);
            if (hk) UnregisterHotKey(Handle, HotkeyId + 1);
            Log("Ctrl+Alt+S свободна для Старт-пака: " + hk);

            if (!Env("STARTPACK_TEST_NOCHECK"))
            {
                await CheckUpdates(true);
                await CheckNet(true);
                Log("связь: " + string.Join(", ", NetSummary()));
                await CheckAllHosts();
            }
            RefreshState();
            Snap(this, testLog + ".main.png");


            if (Env("STARTPACK_TEST_RUN"))
            {
                await RunAllCore();
                Log("запуск всего: " + lblStatus.Text);
                foreach (Item it in cfg.Items) Log("  " + it.Name + " → " + Describe(it).Status);
                Snap(this, testLog + ".run.png");

                // сторож: закрыть программу со сторожем и подождать, пока он её поднимет
                Item w = cfg.Items.FirstOrDefault(i => i.Watch && i.Enabled && !i.Admin && Core.IsRunning(i, Core.RunningNames()));
                if (w != null)
                {
                    foreach (string n in Core.ProcNames(w))
                        foreach (Process p in Process.GetProcessesByName(n)) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                    Log("сторож: закрыл «" + w.Name + "», жду…");
                    bool back = await HappCtl.WaitFor(() => Core.IsRunning(w, Core.RunningNames()), 12000);
                    Log("сторож: «" + w.Name + "» снова работает = " + back);
                }

                // горячая клавиша: такое же сообщение, как при нажатии Ctrl+Alt+S
                PostMessage(Handle, WM_HOTKEY, (IntPtr)HotkeyId, IntPtr.Zero);
                await Task.Delay(400);
                Log("горячая клавиша → «Запустить всё» начат: " + runningAll);
                await HappCtl.WaitFor(() => !runningAll, 30000);
                Log("после горячей клавиши: " + lblStatus.Text);
            }

            // подписи установленных программ — так же проверяются скачанные установщики
            foreach (AppSource a in Installers.All.Where(x => x.Publisher != null))
            {
                string path = Installers.SafeDetect(a);
                if (path == null) { Log("подпись " + a.Name + ": не установлена"); continue; }
                string exe = Core.Expand(path);
                if (exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) exe = Core.ResolveLnk(exe);
                if (Core.IsShell(exe)) { Log("подпись " + a.Name + ": приложение из Store — пропуск"); continue; }
                string why = Installers.CheckSigned(exe, a.Publisher);
                Log("подпись " + a.Name + " (" + System.IO.Path.GetFileName(exe) + "): издатель «" + Installers.SignerName(exe) + "», " + (why == null ? "проверку прошёл" : "НЕ прошёл: " + why));
            }
            string fake = System.IO.Path.Combine(Core.AppDir, "StartPack.exe");
            Log("подпись неподписанного файла (StartPack.exe) как «Discord Inc.»: " + (Installers.CheckSigned(fake, "Discord Inc.") ?? "прошёл — ОШИБКА"));
            string wrongPub = Installers.Get("spotify") != null ? Installers.SafeDetect(Installers.Get("spotify")) : null;
            if (wrongPub != null && !Core.IsShell(wrongPub)) Log("подпись Spotify.exe как «Discord Inc.»: " + (Installers.CheckSigned(Core.Expand(wrongPub), "Discord Inc.") ?? "прошёл — ОШИБКА"));
            Log("установлены: " + string.Join(", ", Installers.All.Select(a => a.Name + "=" + (Installers.SafeDetect(a) != null ? "да" : "нет"))));

            if (Env("STARTPACK_TEST_HASH"))
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StartPack", "hash-test");
                System.IO.Directory.CreateDirectory(dir);
                // 1) совпадение: маленький файл из релиза Koala против отпечатка GitHub
                var small = new AppSource { Name = "Koala (latest.yml)", FileName = "latest.yml", Sha256From = "github:coolcoala/koala-clash" };
                string f1 = System.IO.Path.Combine(dir, "latest.yml");
                await Downloader.Get("https://github.com/coolcoala/koala-clash/releases/latest/download/latest.yml", f1, null, System.Threading.CancellationToken.None);
                Log("контрольная сумма (GitHub, настоящий файл): " + (Installers.CheckSha256(f1, small) ?? "совпала — OK"));
                // 2) подмена: поддельный «установщик INCY» против SHA256SUMS
                AppSource incy = Installers.Get("incy");
                string f2 = System.IO.Path.Combine(dir, incy.FileName);
                System.IO.File.WriteAllText(f2, "это не установщик");
                Log("контрольная сумма (SHA256SUMS, поддельный файл): " + (Installers.CheckSha256(f2, incy) ?? "совпала — ОШИБКА"));
                Log("проверка Koala без подписи идёт по сумме: " + (Installers.Get("koala").Publisher == null && Installers.Get("koala").Sha256From != null));
                foreach (string x in new[] { f1, f2 }) try { System.IO.File.Delete(x); } catch { }
            }

            if (Env("STARTPACK_TEST_KOALA"))
            {
                Item k = cfg.Items.FirstOrDefault(i => i.Special == "koala");
                try
                {
                    KoalaState st = KoalaCtl.GetState(3000);
                    Log("Koala: режим " + st.Mode + ", TUN " + st.Tun + ", группа «" + st.Group + "», серверов " + st.Servers.Count + ", сейчас №" + st.NowIndex + " «" + st.Now + "»");
                    Log("  первые строки: " + string.Join(" | ", st.Servers.Take(3)));
                    string orig = st.Now;
                    k.VpnServer = 2;
                    await KoalaApplyServer(k, false);
                    KoalaState st2 = KoalaCtl.GetState(3000);
                    Log("  после выбора строки №2: сейчас №" + st2.NowIndex + " «" + st2.Now + "» — " + (st.Servers.Count > 1 && st2.Now == st.Servers[1] ? "OK" : "ОШИБКА"));
                    KoalaCtl.Select(st.Group, orig, 3000);
                    KoalaState st3 = KoalaCtl.GetState(3000);
                    Log("  вернул исходный сервер: " + (st3.Now == orig ? "OK" : "ОШИБКА") + ", TUN по-прежнему " + st3.Tun);
                    k.VpnServer = 2;
                    running = Core.RunningNames();
                    RefreshVpn();
                    await Task.Delay(2500);
                    CardInfo ci = Describe(k);
                    Log("  карточка Koala: «" + ci.Status + "», " + ci.Detail + " | кнопки: " + string.Join(", ", ci.Chips.Select(c => c.Value)));
                    Snap(this, testLog + ".koala-card.png");
                    ShowKoalaServers(k);
                    SnapScreen(testLog + ".koala-menu.png");
                    lastMenu.Close();
                }
                catch (Exception ex) { Log("Koala: ОШИБКА " + ex.Message); }
            }

            if (Env("STARTPACK_TEST_OVERLAY"))
            {
                cfg.Overlay = true;
                ApplyOverlay();
                await Task.Delay(1500);
                if (overlay != null)
                {
                    Snap(overlay, testLog + ".overlay.png");
                    cfg.OverlayOpacity = 60;
                    ApplyOverlay();
                    Log("оверлей: показан, " + overlay.Size.Width + "×" + overlay.Size.Height + ", прозрачность " + (int)(overlay.Opacity * 100) + "%");
                }
                cfg.Overlay = false;
                ApplyOverlay();
                Log("оверлей после скрытия: " + (overlay == null ? "закрыт" : "ОШИБКА — остался"));
            }

            if (Env("STARTPACK_TEST_DLUI"))
            {
                Item tg = cfg.Items.FirstOrDefault(i => Installers.KindOf(i) == "tgws");
                if (tg != null)
                {
                    Task<Item> t1 = InstallApp("tgws", tg, true);   // загрузка с прогрессом на карточке
                    await HappCtl.WaitFor(() => tg.Progress > 8, 60000);
                    Snap(this, testLog + ".dl-card.png");
                    Log("карточка во время загрузки: «" + tg.Message + "», кнопки: " + string.Join(", ", Describe(tg).Chips.Select(c => c.Value)));
                    CancelDownload(tg);
                    try { await t1; } catch (Exception ex) { Log("  исключение: " + ex.Message); }
                    Log("после отмены: статус «" + lblStatus.Text + "», карточка занята = " + tg.Busy + ", кнопки: " + string.Join(", ", Describe(tg).Chips.Select(c => c.Value)));
                }
                string tmp2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StartPack", "dlui-test.exe");
                Task t2 = DownloadUi("TG WS Proxy (проверка окна)", "https://github.com/Flowseal/tg-ws-proxy/releases/latest/download/TgWsProxy_windows.exe", tmp2, null);
                await Task.Delay(2500);
                DownloadForm df = Application.OpenForms.OfType<DownloadForm>().FirstOrDefault();
                if (df != null) { Snap(df, testLog + ".dl-window.png"); df.Cts.Cancel(); }
                try { await t2; } catch (Exception ex) { Log("окно загрузки: " + ex.GetType().Name + " после «Отменить», окно закрыто = " + (Application.OpenForms.OfType<DownloadForm>().FirstOrDefault() == null)); }
                foreach (string x in new[] { tmp2, tmp2 + ".part", tmp2 + ".part.etag" }) try { System.IO.File.Delete(x); } catch { }
            }

            if (Env("STARTPACK_TEST_INSTALLDL"))
            {
                // настоящий установщик Spotify (~1 МБ): скачать и проверить подпись, НЕ запуская
                AppSource sp = Installers.Get("spotify");
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StartPack", "installers-test");
                System.IO.Directory.CreateDirectory(tmp);
                string file = System.IO.Path.Combine(tmp, sp.FileName);
                using (var wc = Core.NewClient()) await wc.DownloadFileTaskAsync(new Uri(sp.Url), file);
                Log("скачан установщик Spotify: " + new System.IO.FileInfo(file).Length + " байт, издатель «" + Installers.SignerName(file) + "»");
                Log("  проверка как «Spotify AB»: " + (Installers.CheckSigned(file, "Spotify AB") ?? "прошёл"));
                Log("  проверка как «Discord Inc.»: " + (Installers.CheckSigned(file, "Discord Inc.") ?? "прошёл — ОШИБКА"));
                System.IO.File.Delete(file);
            }

            if (Env("STARTPACK_TEST_AUTOSTART"))
            {
                foreach (Item it in cfg.Items)
                {
                    string e = AutoStartCtl.FindEntry(it);
                    if (e == null || e != "StartPackTest") continue;
                    bool before = AutoStartCtl.IsEnabled(e);
                    AutoStartCtl.SetEnabled(e, false);
                    bool off = AutoStartCtl.IsEnabled(e);
                    AutoStartCtl.SetEnabled(e, true);
                    bool on = AutoStartCtl.IsEnabled(e);
                    Log("свой автозапуск «" + it.Name + "» (запись " + e + "): было " + before + ", после выключения " + off + ", после включения " + on);
                }
            }

            if (Env("STARTPACK_TEST_CLEANUP"))
            {
                var before = Cleanup.Scan(cfg);
                Log("уборка до: " + string.Join(" | ", before.Select(c => c.Title + " — " + c.Hint)));
                using (var f = new CleanupForm(cfg, true, x => MarkExpected(x, true)))
                {
                    await f.Apply();
                    Log("уборка: изменения в списке = " + f.Changed);
                }
                foreach (Item it in cfg.Items.Where(i => i.Repo.Length > 0 && i.UpdateKind == "github-exe"))
                    Log("уборка: TG WS Proxy теперь " + it.Path + ", файл есть = " + System.IO.File.Exists(it.Path) + ", версия " + Core.GetLocalVersion(it));
                Log("уборка после: " + string.Join(" | ", Cleanup.Scan(cfg).Select(c => c.Title)));
            }

            if (Env("STARTPACK_TEST_DIALOGS"))
            {
                foreach (Item it in cfg.Items.Where(i => i.Special.Length > 0 || i.UpdateKind.Length > 0).GroupBy(i => i.Special + i.UpdateKind).Select(g => g.First()))
                    using (var f = new ItemForm(it, "Изменить: " + it.Name)) SnapDialog(f, testLog + ".edit-" + (it.Special.Length > 0 ? it.Special : it.UpdateKind) + ".png");
                Item dsc = cfg.Items.FirstOrDefault(i => AutoStartCtl.FindEntry(i) != null && i.Special.Length == 0 && i.UpdateKind.Length == 0);
                if (dsc != null) using (var f = new ItemForm(dsc, "Изменить: " + dsc.Name)) SnapDialog(f, testLog + ".edit-autostart.png");
                using (var f = new SettingsForm(cfg, null, null)) SnapDialog(f, testLog + ".settings.png");
                using (var f = new HelpForm()) SnapDialog(f, testLog + ".help.png");
                using (var f = new SetupForm()) SnapDialog(f, testLog + ".setup.png");
                foreach (Item v in cfg.Items.Where(VpnCtl.IsVpn).ToList())
                {
                    ShowVpnServers(v, new Point(S(40), S(40)));
                    SnapScreen(testLog + ".server-" + v.Special + ".png");
                    Log("меню сервера " + v.Name + ": " + string.Join(" / ", lastMenu.Items.Cast<ToolStripItem>().Select(x => x.Text)));
                    lastMenu.Close();
                }
                using (var f = new DonateForm(cfg)) SnapDialog(f, testLog + ".donate.png");
                using (var f = new InstallsForm(cfg)) SnapDialog(f, testLog + ".installs.png");
                foreach (DonateAddr d in cfg.Donate)
                {
                    Log("поблагодарить: " + d.Title + ", " + d.Network + ", адрес " + d.Address + ", контрольная сумма верна: " + TonAddress.LooksValid(d.Address));
                    using (Bitmap q = Qr.Render(d.Address, 400)) q.Save(testLog + ".qr.png", ImageFormat.Png);
                }
                using (var f = new SettingsForm(cfg, null, null)) SnapDialog(f, testLog + ".settings2.png");
                using (var f = new JournalForm(() => Journal.Report(cfg, Core.RunningNames(), NetSummary()))) SnapDialog(f, testLog + ".journal.png");
                using (var f = new CleanupForm(cfg, true)) SnapDialog(f, testLog + ".cleanup.png");
                using (var f = new InputForm("Новый профиль", "Название профиля — например, «Игры» или «Работа».", "Игры")) SnapDialog(f, testLog + ".input.png");
                using (var f = PickerForm.Running()) SnapDialog(f, testLog + ".running.png");

                Item z = cfg.Items.FirstOrDefault(i => i.UpdateKind == "zapret");
                if (z != null)
                {
                    ShowItemMenu(z, new Point(cards.Width - S(320), S(40)));
                    SnapScreen(testLog + ".menu-zapret.png");
                    lastMenu.Close();
                }
                ShowProfileMenu();
                SnapScreen(testLog + ".menu-profile.png");
                lastMenu.Close();
                ShowMoreMenu();
                SnapScreen(testLog + ".menu-more.png");
                lastMenu.Close();
                if (tray != null)
                {
                    var tm = Menus.New();
                    BuildTrayMenu(tm);
                    tm.Show(this, new Point(Width - S(320), S(120)));
                    lastMenu = tm;
                    SnapScreen(testLog + ".menu-tray.png");
                    tm.Close();
                }
                using (var bmp = new Bitmap(64, 64))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(32, 32, 32));
                        if (tray != null && tray.Icon != null) g.DrawIcon(tray.Icon, new Rectangle(0, 0, 64, 64));
                    }
                    bmp.Save(testLog + ".tray-icon.png", ImageFormat.Png);
                }

                Theme.Apply(Theme.Current.Dark ? "light" : "dark");
                ApplyTheme();
                Snap(this, testLog + ".other-theme.png");
                Theme.Apply(cfg.Theme);
                ApplyTheme();
            }
            if (Env("STARTPACK_TEST_SIGNAL"))
            {
                var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                var run = new EventWaitHandle(false, EventResetMode.AutoReset);
                ListenSignals(show, run);
                Hide();
                Log("окно спрятано в трей: видно = " + Visible);
                show.Set();
                await Task.Delay(1500);
                Log("сигнал «показать окно» от второго запуска: окно видно = " + Visible);
            }
            Log("=== конец теста");
            exiting = true;
            Close();
        }

        void SnapScreen(string file)
        {
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(120); }
            Rectangle r = Bounds;
            if (lastMenu != null && lastMenu.Visible)
            {
                r = Rectangle.Union(r, lastMenu.Bounds);
                foreach (ToolStripItem ti in lastMenu.Items)
                {
                    var mi = ti as ToolStripMenuItem;
                    if (mi != null && mi.DropDown.Visible) r = Rectangle.Union(r, mi.DropDown.Bounds);
                }
            }
            using (var b = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                b.Save(file, ImageFormat.Png);
            }
        }

        // Окна снимаются отрисовкой «в память»: почти прозрачное окно не мешает тому, кто работает за ПК,
        // и чужие окна поверх не попадают в снимок.
        void SnapDialog(Form f, string file)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(Left + S(40), Top + S(30));
            f.Opacity = 0.01;
            f.Show(this);
            Snap(f, file);
            f.Close();
        }

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // Снимок окна так, как его показывает Windows 11: настоящая рамка и скруглённые углы (для README).
        void SnapFrame(Form f, string file)
        {
            try
            {
                for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(100); }
                RECT wr, fr;
                GetWindowRect(f.Handle, out wr);
                if (DwmGetWindowAttribute(f.Handle, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;
                using (var full = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(full))
                    {
                        IntPtr hdc = g.GetHdc();
                        PrintWindow(f.Handle, hdc, 2 /* PW_RENDERFULLCONTENT */);
                        g.ReleaseHdc(hdc);
                    }
                    var crop = new Rectangle(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top);
                    using (var outBmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(outBmp))
                        {
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            g.Clear(Color.Transparent);
                            using (var path = Gfx.Round(new RectangleF(0, 0, crop.Width, crop.Height), S(8)))
                            using (var tb = new TextureBrush(full, crop))
                                g.FillPath(tb, path);
                        }
                        outBmp.Save(file, ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex) { Log("снимок с рамкой не удался: " + ex.Message); Snap(f, file); }
        }

        void Snap(Form f, string file)
        {
            try
            {
                for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(100); }
                using (var b = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                    b.Save(file, ImageFormat.Png);
                }
            }
            catch (Exception ex) { Log("снимок не удался: " + ex.Message); }
        }
    }
}
