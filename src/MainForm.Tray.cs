// Главное окно: значок в трее, уведомления, сторож, горячая клавиша Ctrl+Alt+S, профили, положение окна.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm
    {
        NotifyIcon tray;
        IntPtr trayHandle = IntPtr.Zero;
        string trayState;
        Action balloonClick;
        bool exiting, trayHintShown;

        // ---------------------------------------------------------------- трей

        void ApplyTray()
        {
            if (cfg.Tray && tray == null)
            {
                tray = new NotifyIcon { Text = Core.Title, Visible = true, ContextMenuStrip = Menus.New() };
                tray.ContextMenuStrip.Opening += (s, e) => BuildTrayMenu(tray.ContextMenuStrip);
                tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
                tray.BalloonTipClicked += delegate { Action a = balloonClick; balloonClick = null; if (a != null) a(); };
                trayState = null;
                UpdateTray(true);
                Journal.Write("трей", "значок в трее включён");
            }
            else if (!cfg.Tray && tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
                Journal.Write("трей", "значок в трее выключен");
            }
        }

        void UpdateTray(bool force = false)
        {
            if (tray == null) return;
            var watched = cfg.Items.Where(i => i.Enabled && Core.ProcNames(i).Length > 0).ToList();
            int up = watched.Count(i => Core.IsRunning(i, running));
            bool failure = cfg.Items.Any(i => i.MessageIsError);
            string state = failure ? "red" : watched.Count == 0 ? "none" : up == watched.Count ? "green" : "amber";
            string text = "Старт-пак — работает " + up + " из " + watched.Count;
            if (text.Length > 63) text = text.Substring(0, 63);
            if (tray.Text != text) tray.Text = text;
            if (!force && state == trayState) return;
            trayState = state;
            Theme t = Theme.MakeDark();
            Color? dot = state == "green" ? t.Green : state == "amber" ? t.Amber : state == "red" ? t.Red : (Color?)null;
            Icon old = tray.Icon;
            tray.Icon = IconLoader.TrayIcon(SystemInformation.SmallIconSize.Width * 2, dot, ref trayHandle);
            if (old != null) old.Dispose();
        }

        void BuildTrayMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem open = Menus.Add(m.Items, "Открыть Старт-пак", null, delegate { ShowFromTray(); });
            open.Font = Fonts.Semi(10f);
            Menus.Add(m.Items, "Запустить всё" + (cfg.Hotkey ? "    Ctrl+Alt+S" : ""), Glyph.Play, delegate { RunAll(); });
            ToolStripMenuItem prof = Menus.Add(m.Items, "Профиль: " + (cfg.Active != null ? cfg.Active.Name : "—"), "", null);
            foreach (Profile p in cfg.Profiles.ToList())
            {
                Profile pp = p;
                var mi = new ToolStripMenuItem(p.Name) { Checked = p == cfg.Active, Padding = new Padding(2, 3, 2, 3) };
                mi.Click += delegate { SwitchProfile(pp.Name); };
                prof.DropDownItems.Add(mi);
            }
            var vpns = cfg.Items.Where(VpnCtl.IsVpn).ToList();
            Item zapret = cfg.Items.FirstOrDefault(i => i.UpdateKind == "zapret");
            if (vpns.Count > 0 || zapret != null) m.Items.Add(new ToolStripSeparator());
            foreach (Item v in vpns)
            {
                Item vv = v;
                bool conn = VpnCtl.IsConnected(v, vpn);
                bool isOpen = Core.IsRunning(v, running);
                if (!isOpen && !v.Enabled && !conn) continue;   // не открыт и не в «Запустить всё» — не захламлять меню
                ToolStripMenuItem sub = Menus.Add(m.Items, v.Name + (conn ? " — подключён" : isOpen ? " — открыт" : ""), conn ? Glyph.Link : "", null);
                Menus.Add(sub.DropDownItems, conn ? "Отключить" : "Подключить", conn ? Glyph.Unlink : Glyph.Link,
                    async delegate { await OnChip(vv, conn ? "disconnect" : "connect"); });
                if (isOpen) Menus.Add(sub.DropDownItems, "Закрыть полностью", Glyph.Stop, async delegate { await StopItem(vv); });
            }
            if (zapret != null) Menus.Add(m.Items, "zapret: перезапустить", Glyph.Refresh, async delegate { await ZapretRestart(zapret); });
            Menus.Add(m.Items, "Проверить связь", Glyph.Globe, async delegate { await CheckNet(true); });
            Menus.Add(m.Items, cfg.Overlay ? "Скрыть оверлей" : "Показать оверлей", "\uE7F4", delegate { ToggleOverlay(); });
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m.Items, "Выйти из Старт-пака", "", delegate { ExitApp(); });
            ThemeSubmenus(m.Items);
        }

        void Notify(string title, string text, Action onClick)
        {
            Journal.Write("уведомление", title + " — " + text);
            Log("УВЕДОМЛЕНИЕ: " + title + " — " + text);
            if (tray == null || testLog != null) { if (Visible) Status(title + ". " + text); return; }
            balloonClick = onClick;
            tray.ShowBalloonTip(6000, title, text, ToolTipIcon.None);
        }

        public void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            MaybeShowAutostartTip();
        }

        void HideToTray()
        {
            SaveBounds();
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                Notify("Старт-пак работает в трее", "Перезапуск закрывшихся программ и Ctrl+Alt+S продолжают работать. Открыть — щелчок по значку возле часов.", ShowFromTray);
            }
        }

        public void ExitApp()
        {
            if (cfg.Items.Any(i => i.Busy && i.Progress >= 0))
            {
                if (!Visible) ShowFromTray();
                Dlg.Info(this, "Подождите немного", "Идёт обновление — выйдите, когда оно закончится.");
                return;
            }
            exiting = true;
            Journal.Write("старт", "выход");
            Close();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            SaveBounds();
            if (!exiting && cfg.Tray && tray != null && e.CloseReason == CloseReason.UserClosing && testLog == null)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (cfg.Items.Any(i => i.Busy && i.Progress >= 0) && e.CloseReason == CloseReason.UserClosing)
            {
                Dlg.Info(this, "Подождите немного", "Идёт обновление — закройте окно, когда оно закончится.");
                e.Cancel = true;
                exiting = false;
            }
        }

        void OnClosedCleanup(object sender, FormClosedEventArgs e)
        {
            UnregisterHotKey(Handle, HotkeyId);
            UnregisterHotKey(Handle, HotkeyId + 2);
            if (overlay != null) { overlay.Close(); overlay = null; }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            signalsStop = true;
        }

        // ---------------------------------------------------------------- положение окна

        void RestoreBounds2()
        {
            if (testLog != null)
            {
                // самопроверка — на втором мониторе, если он есть (основной может быть занят)
                Screen s = Screen.AllScreens.FirstOrDefault(x => !x.Primary) ?? Screen.PrimaryScreen;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(s.WorkingArea.X + S(60), s.WorkingArea.Y + S(30));
                return;
            }
            if (cfg.WinW <= 0 || cfg.WinH <= 0) return;
            var r = new Rectangle(cfg.WinX, cfg.WinY, cfg.WinW, cfg.WinH);
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(10, r.Width - 80), 40)))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
        }

        void SaveBounds()
        {
            if (testLog != null || WindowState != FormWindowState.Normal || !Visible) return;
            if (cfg.WinX == Left && cfg.WinY == Top && cfg.WinW == Width && cfg.WinH == Height) return;
            cfg.WinX = Left; cfg.WinY = Top; cfg.WinW = Width; cfg.WinH = Height;
            SaveCfg();
        }

        // ---------------------------------------------------------------- сторож

        readonly Dictionary<Item, bool> wasRunning = new Dictionary<Item, bool>();
        readonly Dictionary<Item, DateTime> expectedUntil = new Dictionary<Item, DateTime>();
        readonly Dictionary<Item, List<DateTime>> restarts = new Dictionary<Item, List<DateTime>>();

        // Остановка ожидаема (мы сами закрыли/перезапускаем) — сторож молчит 60 секунд.
        void MarkExpected(Item it, bool stopping)
        {
            if (stopping) expectedUntil[it] = DateTime.Now.AddSeconds(60);
            else expectedUntil.Remove(it);
        }

        void Watchdog()
        {
            foreach (Item it in cfg.Items)
            {
                if (!it.Watch || Core.ProcNames(it).Length == 0 || it.Special == "happ") { wasRunning.Remove(it); continue; }
                bool now = Core.IsRunning(it, running);
                bool prev;
                bool known = wasRunning.TryGetValue(it, out prev);
                wasRunning[it] = now;
                if (!known || !prev || now) continue;
                DateTime until;
                if (expectedUntil.TryGetValue(it, out until) && until > DateTime.Now) continue;
                if (it.Busy || runningAll) continue;

                Journal.Write("автоперезапуск", it.Name + " закрылась");
                if (it.Admin)
                {
                    Notify(it.Name + " остановился", "Нажмите, чтобы запустить снова (Windows спросит права администратора).", () => { var _ = RunSelected(it); });
                    continue;
                }
                List<DateTime> list;
                if (!restarts.TryGetValue(it, out list)) restarts[it] = list = new List<DateTime>();
                list.RemoveAll(d => d < DateTime.Now.AddMinutes(-10));
                if (list.Count >= 3)
                {
                    Notify(it.Name + " закрывается снова и снова", "Старт-пак больше не перезапускает её — откройте его и проверьте.", ShowFromTray);
                    continue;
                }
                list.Add(DateTime.Now);
                if (TryLaunch(it) == 0) Notify(it.Name + " закрылась — запустил снова", "Если закрыли её специально, выключите «Перезапускать, если закроется» в меню карточки «⋯».", ShowFromTray);
            }
        }

        // ---------------------------------------------------------------- горячая клавиша

        const int HotkeyId = 0x5350;
        const int WM_HOTKEY = 0x0312;
        bool hotkeyOk;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr h, int id);

        public bool HotkeyWorks { get { return hotkeyOk; } }

        void ApplyHotkey()
        {
            if (!IsHandleCreated) return;
            UnregisterHotKey(Handle, HotkeyId);
            UnregisterHotKey(Handle, HotkeyId + 2);
            hotkeyOk = false;
            if (!cfg.Hotkey || testLog != null) return;
            // Ctrl+Alt+O — показать/скрыть оверлей
            if (!RegisterHotKey(Handle, HotkeyId + 2, 0x2 | 0x1 | 0x4000, (uint)Keys.O)) Journal.Write("горячая клавиша", "Ctrl+Alt+O занята другой программой");
            // Ctrl (2) + Alt (1) + без автоповтора (0x4000), клавиша S
            hotkeyOk = RegisterHotKey(Handle, HotkeyId, 0x2 | 0x1 | 0x4000, (uint)Keys.S);
            Journal.Write("горячая клавиша", hotkeyOk ? "Ctrl+Alt+S включена" : "Ctrl+Alt+S занята другой программой");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyHotkey();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId + 2)
            {
                ToggleOverlay();
                return;
            }
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId)
            {
                Journal.Write("горячая клавиша", "Ctrl+Alt+S — запустить всё");
                if (!runningAll)
                {
                    Notify("Запускаю всё", "Профиль «" + (cfg.Active != null ? cfg.Active.Name : "—") + "».", ShowFromTray);
                    RunAll();
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- второй запуск программы

        volatile bool signalsStop;

        public void ListenSignals(EventWaitHandle show, EventWaitHandle runAll, EventWaitHandle exit = null)
        {
            var th = new Thread(() =>
            {
                var handles = exit != null ? new WaitHandle[] { show, runAll, exit } : new WaitHandle[] { show, runAll };
                while (!signalsStop)
                {
                    int i = WaitHandle.WaitAny(handles, 1000);
                    if (i == WaitHandle.WaitTimeout || signalsStop || IsDisposed) continue;
                    try
                    {
                        if (i == 0) BeginInvoke((Action)ShowFromTray);
                        else if (i == 1) BeginInvoke((Action)(() => { if (!runningAll) RunAll(); }));
                        else BeginInvoke((Action)ExitApp);
                    }
                    catch { }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        // Запуск без окна (автозапуск при входе в Windows, когда включён трей).
        public void StartInTray()
        {
            ApplyTray();
            var _ = Handle;
            BeginInvoke((Action)Startup);
        }

        // ---------------------------------------------------------------- профили

        void ShowProfileMenu()
        {
            ContextMenuStrip m = Menus.New();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            foreach (Profile p in cfg.Profiles.ToList())
            {
                Profile pp = p;
                var mi = new ToolStripMenuItem(p.Name + "   (" + p.Ids.Count(id => cfg.Items.Any(i => i.Id == id)) + ")") { Checked = p == cfg.Active, Padding = new Padding(2, 4, 2, 4) };
                mi.Click += delegate { SwitchProfile(pp.Name); };
                m.Items.Add(mi);
            }
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m.Items, "Новый профиль из текущего…", null, delegate
            {
                string name = InputForm.Ask(this, "Новый профиль", "Название профиля — например, «Игры» или «Работа». В него попадут программы, которые сейчас включены.", "");
                if (string.IsNullOrWhiteSpace(name)) return;
                name = UniqueProfileName(name.Trim());
                cfg.StoreProfile();
                cfg.Profiles.Add(new Profile { Name = name, Ids = cfg.Items.Where(i => i.Enabled).Select(i => i.Id).ToList() });
                SwitchProfile(name);
            });
            Profile cur = cfg.Active;
            if (cur != null)
            {
                Menus.Add(m.Items, "Переименовать «" + cur.Name + "»…", null, delegate
                {
                    string name = InputForm.Ask(this, "Переименовать профиль", "Новое название:", cur.Name);
                    if (string.IsNullOrWhiteSpace(name) || name.Trim() == cur.Name) return;
                    cur.Name = UniqueProfileName(name.Trim());
                    cfg.ActiveProfile = cur.Name;
                    SaveCfg();
                    SyncCards();
                });
                ToolStripMenuItem del = Menus.Add(m.Items, "Удалить «" + cur.Name + "»", null, delegate
                {
                    if (!Ask("Удалить профиль?", "Профиль «" + cur.Name + "» пропадёт. Программы останутся в списке.", "Удалить", "Отмена")) return;
                    cfg.Profiles.Remove(cur);
                    SwitchProfile(cfg.Profiles[0].Name);
                });
                del.Enabled = cfg.Profiles.Count > 1;
                del.Tag = "danger";
            }
            lastMenu = m;
            m.Show(btnProfile, new Point(0, btnProfile.Height + S(2)));
        }

        string UniqueProfileName(string name)
        {
            string n = name;
            for (int k = 2; cfg.Profiles.Any(p => p.Name == n); k++) n = name + " (" + k + ")";
            return n;
        }

        void SwitchProfile(string name)
        {
            cfg.StoreProfile();
            cfg.LoadProfile(name);
            SaveCfg();
            SyncCards();
            Status("Профиль «" + name + "»: в «Запустить всё» — " + cfg.Items.Count(i => i.Enabled) + " из " + cfg.Items.Count + ".");
            Journal.Write("профиль", "выбран «" + name + "»");
        }

        // ---------------------------------------------------------------- журнал самопроверки

        void Log(string s)
        {
            if (testLog == null) return;
            try { File.AppendAllText(testLog, DateTime.Now.ToString("HH:mm:ss ") + s + "\r\n", Encoding.UTF8); } catch { }
        }
    }
}
