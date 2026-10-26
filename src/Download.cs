// Скачивание файлов: прогресс, отмена, защита от зависания и докачка с места обрыва.

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StartPack
{
    public class DlProgress
    {
        public long Done, Total;
        public double Speed;      // байт в секунду
        public int Percent { get { return Total > 0 ? (int)Math.Min(100, Done * 100 / Total) : -1; } }

        public string Text
        {
            get
            {
                string s = Total > 0 ? Mb(Done) + " из " + Mb(Total) + " МБ" : Mb(Done) + " МБ";
                if (Speed > 0) s += " · " + (Speed / 1048576.0).ToString("0.0") + " МБ/с";
                return s;
            }
        }

        static string Mb(long b) { return (b / 1048576.0).ToString(b < 10 * 1048576 ? "0.0" : "0"); }
    }

    public static class Downloader
    {
        public const int StallSeconds = 30;

        // Скачать url в file. Недокачанное лежит в file + ".part" — следующий вызов докачает с того же места.
        public static async Task Get(string url, string file, Action<DlProgress> progress, CancellationToken ct)
        {
            string part = file + ".part";
            string tagFile = part + ".etag";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            string tag = have > 0 && File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "";
            if (have > 0 && tag.Length == 0) { File.Delete(part); have = 0; }   // не знаем, тот ли это файл — качаем заново
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "StartPack/" + Core.Version;
            req.AllowAutoRedirect = true;
            req.Timeout = StallSeconds * 1000;
            req.ReadWriteTimeout = StallSeconds * 1000;
            if (have > 0)
            {
                req.AddRange(have);
                req.Headers["If-Range"] = tag;   // сервер докачает, только если файл не поменялся
            }

            // Сторож: если StallSeconds не приходит ни байта — обрываем запрос.
            long lastTick = DateTime.UtcNow.Ticks;
            bool stalled = false;
            int stallMs = StallSeconds * 1000;
            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            using (new System.Threading.Timer(_ =>
            {
                if (new TimeSpan(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastTick)).TotalMilliseconds > stallMs)
                {
                    stalled = true;
                    try { req.Abort(); } catch { }
                }
            }, null, 1000, 1000))
            {
                HttpWebResponse resp;
                try
                {
                    resp = (HttpWebResponse)await req.GetResponseAsync();
                }
                catch (WebException ex)
                {
                    ct.ThrowIfCancellationRequested();
                    if (stalled) throw new TimeoutException("сервер не ответил за " + StallSeconds + " секунд");
                    var r = ex.Response as HttpWebResponse;
                    // докачка не нужна — файл уже целый (сервер ответил 416 на диапазон за концом)
                    if (r != null && (int)r.StatusCode == 416 && have > 0) { r.Dispose(); Finish(part, file); return; }
                    throw new IOException(Describe(ex));
                }

                using (resp)
                {
                    bool resumed = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed) have = 0;
                    string etag = resp.Headers["ETag"] ?? resp.Headers["Last-Modified"] ?? "";
                    if (etag.Length > 0) File.WriteAllText(tagFile, etag); else if (File.Exists(tagFile)) File.Delete(tagFile);
                    long total = resp.ContentLength > 0 ? resp.ContentLength + have : -1;
                    var p = new DlProgress { Done = have, Total = total };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    long lastReport = -1000, startDone = have;
                    using (Stream s = resp.GetResponseStream())
                    using (var f = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buf = new byte[81920];
                        while (true)
                        {
                            int n;
                            try { n = await s.ReadAsync(buf, 0, buf.Length, ct); }
                            catch (Exception)
                            {
                                ct.ThrowIfCancellationRequested();
                                if (stalled) throw new TimeoutException("загрузка зависла — " + StallSeconds + " секунд не пришло ни байта. Нажмите ещё раз — докачаю");
                                throw new IOException("связь оборвалась на " + p.Text + ". Нажмите ещё раз — докачаю");
                            }
                            Interlocked.Exchange(ref lastTick, DateTime.UtcNow.Ticks);
                            if (n == 0) break;
                            await f.WriteAsync(buf, 0, n, ct);
                            p.Done += n;
                            if (sw.ElapsedMilliseconds - lastReport >= 250)
                            {
                                lastReport = sw.ElapsedMilliseconds;
                                p.Speed = sw.Elapsed.TotalSeconds > 0.5 ? (p.Done - startDone) / sw.Elapsed.TotalSeconds : 0;
                                if (progress != null) progress(p);
                            }
                        }
                    }
                    if (total > 0 && p.Done < total) throw new IOException("связь оборвалась — скачано " + p.Text + ". Нажмите ещё раз — докачаю");
                    if (progress != null) progress(p);
                }
            }
            Finish(part, file);
        }

        static void Finish(string part, string file)
        {
            if (File.Exists(file)) File.Delete(file);
            File.Move(part, file);
            try { File.Delete(part + ".etag"); } catch { }
        }

        static string Describe(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout: return "сервер не отвечает";
                case WebExceptionStatus.NameResolutionFailure: return "нет интернета или сайт недоступен (DNS)";
                case WebExceptionStatus.ConnectFailure: return "не удалось подключиться к серверу";
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.TrustFailure: return "оборвалось шифрованное соединение — похоже на блокировку, попробуйте с zapret или VPN";
                case WebExceptionStatus.ProtocolError:
                    var r = ex.Response as HttpWebResponse;
                    return r != null ? "сервер ответил ошибкой " + (int)r.StatusCode : ex.Message;
                default: return ex.Message;
            }
        }
    }

    // Окно загрузки — когда качаем не для карточки (например, установка из «Установить программы»).
    public class DownloadForm : ThemedForm
    {
        readonly Label info;
        readonly Panel bar;
        int percent = -1;
        public readonly CancellationTokenSource Cts = new CancellationTokenSource();

        public DownloadForm(string what)
        {
            Text = "Загрузка";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ControlBox = false;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(S(24), S(18), S(24), S(20)) };
            Controls.Add(t);
            Label head = Lbl("Скачиваю " + what, "title");
            head.MaximumSize = new Size(S(440), 0);
            head.Margin = new Padding(0, 0, 0, S(12));
            t.Controls.Add(head);
            bar = new Panel { Size = new Size(S(420), S(8)), Margin = new Padding(0, 0, 0, S(8)) };
            bar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.Clear(Theme.Current.Bg);
                var r = new RectangleF(0, 0, bar.Width - 1, bar.Height - 1);
                Gfx.Fill(e.Graphics, r, r.Height / 2, Theme.Current.SurfaceAlt);
                if (percent > 0) Gfx.Fill(e.Graphics, new RectangleF(0, 0, Math.Max(r.Height, r.Width * percent / 100f), r.Height), r.Height / 2, Theme.Current.Accent);
            };
            t.Controls.Add(bar);
            info = Lbl("подключаюсь…", "dim");
            info.Margin = new Padding(0, 0, 0, S(14));
            t.Controls.Add(info);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var cancel = new FlatBtn("Отменить", BtnKind.Secondary, null);
            cancel.Fit();
            cancel.Click += delegate { Cts.Cancel(); info.Text = "отменяю…"; };
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
        }

        public void Report(DlProgress p)
        {
            if (IsDisposed) return;
            percent = p.Percent;
            info.Text = (percent >= 0 ? percent + "% · " : "") + p.Text;
            bar.Invalidate();
        }
    }
}
