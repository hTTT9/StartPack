// Главное окно: оверлей поверх окон и фоновая проверка обновлений с напоминанием из трея.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm
    {
        OverlayForm overlay;

        public void ApplyOverlay()
        {
            if (cfg.Overlay)
            {
                if (overlay == null || overlay.IsDisposed)
                {
                    overlay = new OverlayForm();
                    overlay.OpenMain += ShowFromTray;
                    overlay.HideMe += ToggleOverlay;
                    overlay.Moved += (x, y) => { cfg.OverlayX = x; cfg.OverlayY = y; SaveCfg(); };
                    overlay.OpenSettings += ShowOverlaySettings;
                    overlay.RefreshNet += async () => await CheckNet(true);
                    overlay.RowAction += async (tag, stop) =>
                    {
                        Item it = tag as Item;
                        if (it == null || !cfg.Items.Contains(it)) return;
                        if (stop) await StopItem(it); else await RunSelected(it);
                    };
                    overlay.Apply(cfg);
                    UpdateOverlay();
                    overlay.Location = OverlayStart(overlay.Size);
                    overlay.Show();
                }
                overlay.Apply(cfg);
                UpdateOverlay();
            }
            else if (overlay != null)
            {
                overlay.Close();
                overlay.Dispose();
                overlay = null;
            }
        }

        Point OverlayStart(Size size)
        {
            if (cfg.OverlayX >= 0 && cfg.OverlayY >= 0 &&
                Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(cfg.OverlayX + 20, cfg.OverlayY + 10))))
                return new Point(cfg.OverlayX, cfg.OverlayY);
            Screen scr = testLog != null ? (Screen.AllScreens.FirstOrDefault(x => !x.Primary) ?? Screen.PrimaryScreen) : Screen.PrimaryScreen;
            return new Point(scr.WorkingArea.Right - size.Width - S(16), scr.WorkingArea.Top + S(16));
        }

        public void ToggleOverlay()
        {
            cfg.Overlay = !cfg.Overlay;
            SaveCfg();
            ApplyOverlay();
            Journal.Write("оверлей", cfg.Overlay ? "показан" : "скрыт");
        }

        void UpdateOverlay()
        {
            if (overlay == null || overlay.IsDisposed) return;
            var rows = new List<OverlayRow>();
            foreach (Item i in cfg.Items)
            {
                CardInfo ci = Describe(i);
                bool show = cfg.OverlayShow == "all" || (cfg.OverlayShow == "running" ? ci.Running || ci.Dot == Theme.Current.Green : i.Enabled);
                if (!show) continue;
                rows.Add(new OverlayRow
                {
                    Name = i.Name, Status = ci.Status, Dot = ci.Dot, Tag = i, Running = ci.Running,
                    CanAct = !i.Busy && Core.ProcNames(i).Length > 0 && Core.PathExists(i.Path)
                });
            }
            overlay.SetData(rows, cfg.OverlayNet ? netTargets : null, netChecking);
        }

        public void ShowOverlaySettings()
        {
            if (!Visible) ShowFromTray();
            using (var f = new OverlaySettingsForm(cfg, () => { ApplyOverlay(); }))
                f.ShowDialog(this);
            SaveCfg();
        }

        // ---------------------------------------------------------------- фоновая проверка обновлений

        System.Windows.Forms.Timer updateTimer;
        readonly HashSet<string> notifiedUpdates = new HashSet<string>();

        void StartBackgroundUpdates()
        {
            if (testLog != null) return;
            updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };   // раз в 6 часов
            updateTimer.Tick += async delegate
            {
                if (!cfg.CheckUpdatesOnStart || runningAll || cfg.Items.Any(i => i.Busy)) return;
                await CheckUpdates(false);
                await CheckSelfUpdate(false);
                NotifyUpdates();
            };
            updateTimer.Start();
        }

        // Напомнить из трея о новых версиях — по одному разу на версию.
        void NotifyUpdates()
        {
            if (!cfg.UpdateNotify) return;
            var fresh = cfg.Items.Where(i => i.UpdateAvailable && Core.PathExists(i.Path) && notifiedUpdates.Add(i.Name + " " + i.LatestTag)).ToList();
            if (fresh.Count == 0 || Visible) return;
            Notify("Есть обновления", string.Join(", ", fresh.Select(i => i.Name + " " + Core.CleanTag(i.LatestTag))) + ". Нажмите, чтобы открыть Старт-пак.", ShowFromTray);
        }
    }
}
