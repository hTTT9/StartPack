// Точка входа.
//   /autorun     — запустить всё (при входе в Windows; если включён трей — без окна);
//   /tray        — сразу в трей; /exit — попросить работающий Старт-пак выйти;
//   /uninstall   — удалить Старт-пак (так его вызывает «Параметры → Приложения»);
//   /test <лог>  — самопроверка со снимками окон (для разработки).
// Второй запуск программы не открывает второе окно, а показывает первое (или запускает всё — с /autorun).

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace StartPack
{
    static class Program
    {
        const string ShowEvent = @"Local\StartPack-show";
        const string RunEvent = @"Local\StartPack-runall";
        const string ExitEvent = @"Local\StartPack-exit";

        [STAThread]
        static void Main(string[] args)
        {
            bool autorun = args.Any(a => a.Equals("/autorun", StringComparison.OrdinalIgnoreCase));
            // /exit — попросить работающий Старт-пак выйти (нужно установщику, чтобы заменить файл)
            if (args.Any(a => a.Equals("/exit", StringComparison.OrdinalIgnoreCase)))
            {
                try { using (var e = EventWaitHandle.OpenExisting(ExitEvent)) e.Set(); } catch { }
                return;
            }
            string testLog = null;
            int ti = Array.FindIndex(args, a => a.Equals("/test", StringComparison.OrdinalIgnoreCase));
            if (ti >= 0 && ti + 1 < args.Length) testLog = args[ti + 1];
            bool trayOnly = args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

            SelfUpdate.JustUpdated = args.Any(a => a.Equals("/updated", StringComparison.OrdinalIgnoreCase));
            // /update — скачанная новая версия ставит себя на место старой и открывается
            if (testLog == null && args.Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase)))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Setup.SilentUpdate();
                return;
            }
            if (testLog == null && (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) || args.Any(a => a.Equals("/install", StringComparison.OrdinalIgnoreCase)) || (!autorun && !trayOnly)))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // /uninstall — из «Параметры → Приложения»
                if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) { Setup.UninstallInteractive(); return; }
                // запуск не из папки установки (из архива, из «Загрузок») — предложить установиться
                if (Setup.OfferInstall()) return;
            }

            bool created;
            string mutexName = testLog == null ? "StartPack-single-instance" : "StartPack-test-" + System.Diagnostics.Process.GetCurrentProcess().Id;
            using (var m = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    // Старт-пак уже работает — попросить его показать окно или запустить всё.
                    try { using (var e = EventWaitHandle.OpenExisting(autorun ? RunEvent : ShowEvent)) e.Set(); } catch { }
                    return;
                }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
                string warning;
                Config cfg = Core.Load(out warning);
                Theme.Apply(cfg.Theme);
                // /tray — сразу в трей, ничего не запуская (так установщик возвращает Старт-пак после обновления)
                bool hidden = (autorun || trayOnly) && cfg.Tray && testLog == null && warning == null;

                var form = new MainForm(cfg, autorun, hidden, testLog, warning);
                if (testLog == null)
                {
                    var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
                    var run = new EventWaitHandle(false, EventResetMode.AutoReset, RunEvent);
                    var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEvent);
                    form.ListenSignals(show, run, exit);
                }
                var ctx = new ApplicationContext();
                form.FormClosed += delegate { ctx.ExitThread(); };
                if (hidden) form.StartInTray();
                else form.Show();
                Application.Run(ctx);
            }
        }
    }
}
