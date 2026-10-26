// Главное окно: установка недостающих программ (zapret, TG WS Proxy, Happ, Spotify, Telegram, Discord).

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public partial class MainForm
    {
        void ShowInstalls()
        {
            using (var f = new InstallsForm(cfg))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Chosen == null) return;
                AppSource a = Installers.Get(f.Chosen);
                if (f.ChosenAction == "add")
                {
                    Item it = null;
                    try { it = a.Template(); } catch { }
                    if (it != null) AddViaEditor(it, "Добавить: " + it.Name);
                }
                else { var _ = InstallApp(f.Chosen, null, false); }
            }
        }

        // Понятная причина по коду выхода установщика (Inno Setup, NSIS, MSI, Squirrel).
        static string InstallerFailure(int code)
        {
            if (code == int.MinValue || code == 0)
                return "установщик закрылся, но программа не появилась — возможно, её поставили в другую папку. Попробуйте «⋯» → «Найти программы заново» или укажите путь в «Изменить…»";
            switch (code)
            {
                case 1: return "установка отменена или прервана (код 1)";
                case 2: return "установка отменена пользователем (код 2)";
                case 5: return "нет доступа — установщику не дали права администратора (код 5)";
                case 1223: return "отменено — Windows не получила разрешение на установку (код 1223)";
                case 1602: return "установка отменена пользователем (код 1602)";
                case 1603: return "установщик сообщил о внутренней ошибке (код 1603) — попробуйте ещё раз или перезагрузите ПК";
                case 1618: return "уже идёт другая установка (код 1618) — дождитесь её окончания";
                default: return "установщик завершился с ошибкой (код " + code + ")";
            }
        }

        static string Capital(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

        // «Готовые пункты»: если программы нет на ПК — предложить установить.
        async Task AddTemplate(string id)
        {
            AppSource a = Installers.Get(id);
            if (a == null) return;
            Item it = null;
            try { it = a.Template(); } catch { }
            if (it == null)
            {
                if (Ask(a.Name + " не найдена", "На этом ПК нет " + a.Name + ". Установить с официального источника (" + a.Host + ")?", "Установить", "Отмена"))
                    await InstallApp(id, null, true);
                return;
            }
            if (cfg.Items.Any(x => SameKind(x, it)) && !Ask("Уже есть в списке", "«" + it.Name + "» уже есть в списке. Добавить ещё один?", "Добавить", "Отмена")) return;
            AddViaEditor(it, "Добавить: " + it.Name);
        }

        // target — карточка, у которой пропал файл: после установки ей обновится путь.
        async Task<Item> InstallApp(string id, Item target, bool asked)
        {
            AppSource a = Installers.Get(id);
            if (a == null) return null;
            Item made = null;
            string failed = null;   // причина неудачи — показывается на карточке или в окне
            Journal.Write("установка", a.Name + ": начало");
            try
            {
                if (id == "zapret" || id == "tgws")
                {
                    if (!asked && !Ask("Скачать " + a.Name + "?", "Старт-пак скачает последнюю версию " + a.Name + " с GitHub (" + a.Host + ")" +
                        (id == "zapret" ? " в новую папку на рабочем столе." : " в свою папку."), "Скачать", "Отмена")) return null;
                    made = id == "zapret" ? await DownloadZapretFresh(target) : await DownloadTgFresh(target);
                }
                else
                {
                    string check = a.Publisher != null ? "проверит цифровую подпись («" + a.Publisher + "»)" : "сверит контрольную сумму с опубликованной разработчиком";
                    if (!asked && !Ask("Установить " + a.Name + "?", "Старт-пак скачает официальный установщик с " + a.Host + ", " + check + " и запустит. Дальше — обычная установка программы.", "Скачать и установить", "Отмена")) return null;
                    string dir = Path.Combine(Path.GetTempPath(), "StartPack", "installers");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, a.FileName);
                    await DownloadUi("установщик " + a.Name, a.Url, file, target);
                    if (target != null) { target.Busy = true; SetMsg(target, "проверяю установщик…", false); }

                    string why = await Task.Run(() => Installers.Verify(file, a));
                    if (why != null)
                    {
                        try { File.Delete(file); } catch { }
                        failed = "установщик не прошёл проверку: " + why + ". Ничего не запущено, файл удалён";
                    }
                    else
                    {
                        Journal.Write("установка", a.Name + ": " + (a.Publisher != null ? "подпись «" + Installers.SignerName(file) + "» верна" : "контрольная сумма совпала") + ", запускаю установщик");
                        Status("Идёт установка " + a.Name + " — следуйте окну установщика.");
                        if (target != null) SetMsg(target, "идёт установка — следуйте окну установщика…", false);
                        int exitCode = int.MinValue;
                        Process p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = dir });
                        if (p != null)
                            using (p)
                            {
                                await Task.Run(() => p.WaitForExit(30 * 60 * 1000));
                                try { if (p.HasExited) exitCode = p.ExitCode; } catch { }
                            }
                        // некоторые установщики закрываются раньше, чем программа появилась на диске
                        bool ok = await HappCtl.WaitFor(() => Installers.SafeDetect(a) != null, exitCode == 0 || exitCode == int.MinValue ? 90000 : 5000);
                        try { File.Delete(file); } catch { }
                        if (ok) made = a.Template();
                        else failed = InstallerFailure(exitCode);
                    }
                }
            }
            catch (Win32Exception ex) { failed = ex.NativeErrorCode == 1223 ? "отменено — Windows не получила разрешение на установку" : "установщик не запустился: " + ex.Message; }
            catch (OperationCanceledException) { Status("Загрузка " + a.Name + " отменена — нажмите «Установить» ещё раз, чтобы продолжить."); return null; }
            catch (Exception ex) { failed = (ex is IOException || ex is TimeoutException || ex is WebException ? "не скачалось: " : "ошибка: ") + ex.Message; }
            finally
            {
                if (target != null) { target.Busy = false; target.Progress = -1; if (!target.MessageIsError) SetMsg(target, null, false); }
            }
            if (failed != null)
            {
                Journal.Write("установка", a.Name + ": не установлена — " + failed);
                Status(a.Name + ": не установлена — " + failed + ".");
                if (target != null) SetMsg(target, "не установлена: " + failed, true);
                else Dlg.Info(this, "Не получилось установить " + a.Name, Capital(failed) + ".\n\nПодробности — в «⋯» → «Журнал и отчёт».");
                return null;
            }
            if (made == null) return null;
            Core.ResetShellCache();
            Journal.Write("установка", a.Name + " установлена: " + made.Path);

            if (target != null && cfg.Items.Contains(target))
            {
                string path = made.Path;
                // у zapret сохранить выбранную стратегию, если она есть в новой версии
                if (id == "zapret")
                {
                    string same = Path.Combine(Core.SafeDir(made.Path) ?? "", Core.SafeFileName(Core.Expand(target.Path)));
                    if (File.Exists(same)) path = same;
                }
                target.Path = path;
                if (!VpnCtl.IsVpn(target)) target.Enabled = true;
                if (target.UpdateKind.Length > 0) target.Watch = true;
                if (target.Args.Length == 0) target.Args = made.Args;
                if (target.ProcessName.Length == 0) target.ProcessName = made.ProcessName;
                target.InstalledTag = made.InstalledTag;
                target.InstalledSize = made.InstalledSize;
                target.LocalVersion = null;
                icons.Remove(target);
                SaveCfg();
                SyncCards();
                Status(a.Name + " установлена — карточка обновлена.");
                return target;
            }
            Status(a.Name + " установлена.");
            AddViaEditor(made, "Добавить: " + made.Name);
            return made;
        }
    }
}
