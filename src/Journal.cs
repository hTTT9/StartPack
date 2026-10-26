// Журнал: что, когда и с каким результатом делал Старт-пак. Файл logs\startpack.log рядом с программой.
// «Скопировать отчёт» собирает журнал, список программ и сведения о системе в один текст.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StartPack
{
    public static class Journal
    {
        static readonly object sync = new object();
        static readonly List<string> recent = new List<string>();
        public static event Action<string> Added;

        public static string Dir { get { return Path.Combine(Core.AppDir, "logs"); } }
        public static string FilePath { get { return Path.Combine(Dir, "startpack.log"); } }

        public static void Write(string area, string text)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + area + "] " + (text ?? "").Replace("\r", " ").Replace("\n", " ");
            lock (sync)
            {
                recent.Add(line);
                if (recent.Count > 1000) recent.RemoveRange(0, recent.Count - 1000);
                if (!Core.Frozen) try
                {
                    Directory.CreateDirectory(Dir);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024)
                    {
                        string old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, line + "\r\n", Encoding.UTF8);
                }
                catch { }
            }
            Action<string> h = Added;
            if (h != null) try { h(line); } catch { }
        }

        // Последние строки: из памяти, а если программа только что запущена — из файла.
        public static List<string> Tail(int n)
        {
            lock (sync)
            {
                var lines = new List<string>();
                try
                {
                    if (File.Exists(FilePath + ".1") && recent.Count < n) lines.AddRange(File.ReadAllLines(FilePath + ".1", Encoding.UTF8));
                    if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath, Encoding.UTF8));
                }
                catch { lines = recent.ToList(); }
                if (lines.Count == 0) lines = recent.ToList();
                return lines.Skip(Math.Max(0, lines.Count - n)).ToList();
            }
        }

        public static string Report(Config cfg, HashSet<string> running, IEnumerable<string> net)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Старт-пак " + Core.Version + " — отчёт от " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine("Windows: " + WindowsName() + ", .NET " + Environment.Version);
            sb.AppendLine("Экраны: " + string.Join("; ", Screen.AllScreens.Select(s => s.Bounds.Width + "×" + s.Bounds.Height + (s.Primary ? " (основной)" : ""))));
            sb.AppendLine("Тема: " + cfg.Theme + ", трей: " + Yes(cfg.Tray) + ", Ctrl+Alt+S: " + Yes(cfg.Hotkey) + ", профиль: " + cfg.ActiveProfile);
            sb.AppendLine();
            sb.AppendLine("Программы:");
            foreach (Item it in cfg.Items)
            {
                bool run = Core.ProcNames(it).Length > 0 && Core.IsRunning(it, running);
                sb.Append("  • ").Append(it.Name).Append(it.Enabled ? "" : " (выключена)").Append(" — ").Append(run ? "работает" : "не запущена");
                if (it.Special.Length > 0) sb.Append(", тип ").Append(it.Special);
                if (it.UpdateKind.Length > 0)
                    sb.Append(", версия ").Append(it.LocalVersion ?? Core.GetLocalVersion(it)).Append(string.IsNullOrEmpty(it.LatestTag) ? "" : " (на GitHub " + Core.CleanTag(it.LatestTag) + ")");
                if (it.Watch) sb.Append(", автоперезапуск");
                if (it.Admin) sb.Append(", админ");
                if (it.Special == "happ") sb.Append(", сохранённых серверов ").Append(it.HappPresets.Count);
                sb.AppendLine();
                sb.Append("      ").Append(it.Path).Append(it.Args.Length > 0 ? "  " + it.Args : "").Append("  | процесс: ").AppendLine(it.ProcessName);
            }
            List<string> netLines = net == null ? new List<string>() : net.ToList();
            if (netLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Связь: " + string.Join(", ", netLines));
            }
            sb.AppendLine();
            sb.AppendLine("Журнал (последние 150 строк):");
            foreach (string l in Tail(150)) sb.AppendLine(l);
            return sb.ToString();
        }

        static string Yes(bool b) { return b ? "да" : "нет"; }

        static string WindowsName()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = k.GetValue("ProductName") as string ?? "Windows";
                    string build = k.GetValue("CurrentBuild") as string ?? "";
                    string disp = k.GetValue("DisplayVersion") as string ?? "";
                    int b;
                    if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    return name + " " + disp + " (сборка " + build + ")";
                }
            }
            catch { return Environment.OSVersion.ToString(); }
        }
    }
}
