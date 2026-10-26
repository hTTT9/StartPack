// Главное окно: скачивание с прогрессом и отменой — на карточке или в отдельном окошке.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StartPack
{
    public partial class MainForm
    {
        readonly Dictionary<Item, CancellationTokenSource> downloads = new Dictionary<Item, CancellationTokenSource>();

        // card — карточка, на которой показать прогресс и кнопку «Отменить»; null — открыть окно загрузки.
        // Отмена — OperationCanceledException; зависание или обрыв — исключение с понятным текстом.
        async Task DownloadUi(string what, string url, string file, Item card)
        {
            CancellationTokenSource cts;
            DownloadForm dlg = null;
            if (card != null)
            {
                cts = new CancellationTokenSource();
                downloads[card] = cts;
                card.Busy = true;
                card.Progress = 0;
                SetMsg(card, "подключаюсь…", false);
            }
            else
            {
                dlg = new DownloadForm(what);
                dlg.Show(this);
                cts = dlg.Cts;
            }
            Journal.Write("загрузка", what + ": " + url);
            try
            {
                await Downloader.Get(url, file, p =>
                {
                    string text = (p.Percent >= 0 ? p.Percent + "% · " : "") + p.Text;
                    Status("Скачиваю " + what + " — " + text);
                    if (card != null) { card.Progress = Math.Max(0, p.Percent); SetMsg(card, "скачиваю " + text, false); }
                    if (dlg != null) dlg.Report(p);
                }, cts.Token);
                Journal.Write("загрузка", what + ": готово");
            }
            catch (Exception ex)
            {
                if (cts.IsCancellationRequested)
                {
                    Journal.Write("загрузка", what + ": отменено");
                    Status("Загрузка " + what + " отменена.");
                    throw new OperationCanceledException();
                }
                Journal.Write("загрузка", what + ": не удалась — " + ex.Message);
                throw;
            }
            finally
            {
                if (card != null) { downloads.Remove(card); card.Progress = -1; }
                if (dlg != null) { dlg.Close(); dlg.Dispose(); }
            }
        }

        void CancelDownload(Item it)
        {
            CancellationTokenSource cts;
            if (!downloads.TryGetValue(it, out cts)) return;
            cts.Cancel();
            SetMsg(it, "отменяю…", false);
        }
    }
}
