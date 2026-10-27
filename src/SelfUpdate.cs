// Обновление самого Старт-пака: выпуски на GitHub (hTTT9/StartPack).
// В выпуске лежат StartPack.exe, архив для людей и SHA256SUMS.txt; GitHub ещё и сам публикует SHA-256 каждого файла.
// Скачанный exe проверяется (контрольная сумма и номер версии внутри файла), запускается с /update —
// он ставит себя на место старого (Setup.Install) и открывается. Список программ и настройки не трогаются.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StartPack
{
    public static class SelfUpdate
    {
        public const string Repo = "hTTT9/StartPack";
        public const string ExeName = "StartPack.exe";
        public static string Page { get { return "https://github.com/" + Repo; } }
        public static string ReleasesPage { get { return Page + "/releases"; } }

        // самопроверка: свой адрес вместо api.github.com
        static string Api
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("STARTPACK_TEST_UPDATE_API");
                return string.IsNullOrEmpty(t) ? "https://api.github.com/repos/" + Repo + "/releases/latest" : t;
            }
        }

        public class Info
        {
            public string Tag, Version, ExeUrl, Sha256, SumsUrl, Notes, PageUrl;
            public bool Newer { get { return Core.CompareVersions(Version, Core.Version) > 0; } }
        }

        public static Info Last;          // результат последней проверки
        public static bool JustUpdated;   // запущены сразу после обновления
        public static string LastError;   // почему проверить не вышло

        // null — выпусков ещё нет.
        public static async Task<Info> Check()
        {
            LastError = null;
            try
            {
                string json;
                using (WebClient wc = Core.NewClient())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = await wc.DownloadStringTaskAsync(Api);
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var i = new Info { Tag = Convert.ToString(d["tag_name"]) };
                i.Version = Core.CleanTag(i.Tag);
                object v;
                if (d.TryGetValue("body", out v) && v != null)
                {
                    // в окне обновления строка «Как установить…» со страницы выпуска не нужна
                    string notes = Convert.ToString(v);
                    int cut = notes.IndexOf("Как установить:", StringComparison.Ordinal);
                    i.Notes = (cut >= 0 ? notes.Substring(0, cut) : notes).Trim();
                }
                if (d.TryGetValue("html_url", out v) && v != null) i.PageUrl = Convert.ToString(v);
                if (d.TryGetValue("assets", out v) && v is IEnumerable)
                    foreach (object o in (IEnumerable)v)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null) continue;
                        string name = Convert.ToString(a["name"]);
                        string url = Convert.ToString(a["browser_download_url"]);
                        if (name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                        {
                            i.ExeUrl = url;
                            object dg;
                            if (a.TryGetValue("digest", out dg) && dg != null && Convert.ToString(dg).StartsWith("sha256:"))
                                i.Sha256 = Convert.ToString(dg).Substring(7).ToLowerInvariant();
                        }
                        else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) i.SumsUrl = url;
                    }
                Last = i;
                return i;
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) { Last = null; LastError = "на GitHub ещё нет выпусков"; return null; }
                LastError = ex.Message;
                throw;
            }
            catch (Exception ex) { LastError = ex.Message; throw; }
        }

        static string Sha256Of(string file)
        {
            using (var fs = File.OpenRead(file))
            using (var h = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // null — всё в порядке, иначе причина, почему ставить нельзя.
        public static string Verify(string file, Info i)
        {
            string expected = i.Sha256;
            if (expected == null && i.SumsUrl != null)
            {
                try
                {
                    using (WebClient wc = Core.NewClient())
                        foreach (string line in wc.DownloadString(i.SumsUrl).Split('\n'))
                        {
                            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                            if (m.Success && m.Groups[2].Value.Trim().Equals(ExeName, StringComparison.OrdinalIgnoreCase)) expected = m.Groups[1].Value.ToLowerInvariant();
                        }
                }
                catch (Exception ex) { return "не удалось получить контрольную сумму: " + ex.Message; }
            }
            if (expected == null) return "в выпуске нет контрольной суммы — ставить такой файл небезопасно";
            if (Sha256Of(file) != expected) return "контрольная сумма не совпадает — файл повреждён при загрузке или подменён";
            string inside = null;
            try { inside = Core.NormalizeVer(FileVersionInfo.GetVersionInfo(file).FileVersion); } catch { }
            if (inside == null || Core.CompareVersions(inside, i.Version) != 0)
                return "внутри файла версия " + (inside ?? "?") + ", а выпуск называется " + i.Version;
            return null;
        }

        public static string TempFile(Info i)
        {
            string dir = Path.Combine(Path.GetTempPath(), "StartPack", "update");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "StartPack-" + i.Version + ".exe");
        }

        // Новая версия ставит себя сама (StartPack.exe /update) — этот экземпляр должен сразу выйти.
        public static void Launch(string file)
        {
            Journal.Write("обновление", "запускаю установку " + file);
            Process.Start(new ProcessStartInfo(file, "/update") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(file) });
        }
    }
}
