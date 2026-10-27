// Главное окно: VPN-клиенты (Happ, INCY, Koala Clash) — запуск, подключение, выбор сервера.

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
        VpnSnapshot vpn = new VpnSnapshot();
        bool koalaPolling;

        // Раз в 2 секунды: кто из VPN подключён. Koala опрашивается в фоне (через её канал).
        void RefreshVpn()
        {
            bool h, i;
            VpnCtl.XrayOwners(running, out h, out i);
            vpn.Happ = h;
            vpn.Incy = IncyCtl.Connected(cfg.Items.FirstOrDefault(x => x.Special == "incy"), running, i);
            vpn.Warp = cfg.Items.Any(x => x.Special == "warp") && WarpCtl.Connected();
            vpn.KoalaRunning = KoalaCtl.AppRunning(running);
            if (!vpn.KoalaRunning) { vpn.Koala = null; return; }
            if (koalaPolling || !cfg.Items.Any(x => x.Special == "koala")) return;
            koalaPolling = true;
            Task.Run(() => { try { return KoalaCtl.GetState(1500); } catch { return null; } })
                .ContinueWith(t => { vpn.Koala = t.Result; koalaPolling = false; cards.Invalidate(); UpdateOverlay(); },
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        async Task<bool> ConnectedNow(Item it)
        {
            if (it.Special == "warp") { vpn.Warp = await Task.Run(() => WarpCtl.Connected()); return vpn.Warp; }
            if (it.Special == "koala")
            {
                KoalaState st = await Task.Run(() => { try { return KoalaCtl.GetState(2000); } catch { return null; } });
                vpn.Koala = st;
                return st != null && st.Tun;
            }
            bool h, i;
            HashSet<string> now = Core.RunningNames();
            VpnCtl.XrayOwners(now, out h, out i);
            if (it.Special == "incy") { vpn.Incy = await Task.Run(() => IncyCtl.Connected(it, now, i)); return vpn.Incy; }
            return h;
        }

        async Task<int> RunVpn(Item it, bool inSequence)
        {
            string title = VpnCtl.Title(it.Special);
            if (Core.IsRunning(it, running))
            {
                // клиент уже открыт — не перезапускаем, только подключаем, если нужно
                if (it.HappAutoConnect && !await ConnectedNow(it)) return await VpnConnect(it) ? 0 : 2;
                if (it.Special == "koala" && it.VpnServer > 0) await KoalaApplyServer(it, false);
                SetMsg(it, null, false);
                return 1;
            }
            if (it.Special == "happ")
            {
                HappPreset p = it.FindPreset();
                if (p != null && !HappCtl.IsCurrent(p))
                {
                    try { HappCtl.Apply(p); Journal.Write("happ", "подставлен сохранённый сервер «" + p.Name + "»"); }
                    catch (Exception ex) { Journal.Write("happ", "не удалось подставить сервер: " + ex.Message); }
                }
            }
            if (it.Special == "koala" && it.HappAutoConnect && KoalaCtl.PresetTun(true))
                Journal.Write("koala", "в настройках Koala включён TUN — подключится при старте");
            if (it.Special == "incy" && it.HappAutoConnect && IncyCtl.PresetAutoConnect(it, true))
                Journal.Write("incy", "в настройках INCY включено «подключаться при запуске»");

            int r = TryLaunch(it);
            if (r != 0) return r;
            if (!it.HappAutoConnect)
            {
                if (inSequence && it.Delay > 0) await Task.Delay(it.Delay * 1000);
                return 0;
            }
            it.Busy = true;
            SetMsg(it, "жду, пока " + title + " откроется…", false);
            await HappCtl.WaitFor(() => Core.IsRunning(it, Core.RunningNames()), 20000);
            if (it.Special == "koala") await HappCtl.WaitFor(KoalaCtl.PipeExists, 25000);
            await Task.Delay(3000);
            if (it.Special == "incy")
            {
                // INCY подключается сам (его настройка «подключаться при запуске») — подождать, прежде чем слать команду
                SetMsg(it, "жду, пока INCY подключится…", false);
                if (await WaitConnected(it, 15000))
                {
                    it.Busy = false;
                    SetMsg(it, null, false);
                    Journal.Write("incy", "подключён");
                    RefreshState();
                    return 0;
                }
            }
            return await VpnConnect(it) ? 0 : 2;
        }

        async Task<bool> VpnConnect(Item it)
        {
            string title = VpnCtl.Title(it.Special);
            it.Busy = true;
            SetMsg(it, "подключаю…", false);
            bool ok = false;
            string err = null;
            try
            {
                switch (it.Special)
                {
                    case "happ":
                        ok = await HappCtl.Connect();
                        break;
                    case "incy":
                        if (await ConnectedNow(it)) { ok = true; break; }
                        IncyCtl.Send(it, "connect");
                        ok = await WaitConnected(it, 20000);
                        if (!ok) { IncyCtl.Send(it, "connect"); ok = await WaitConnected(it, 12000); }
                        break;
                    case "warp":
                        string werr = null;
                        try { await Task.Run(() => WarpCtl.Run("connect")); }
                        catch (Exception ex) { werr = ex.Message; }
                        if (werr != null && WarpCtl.IsServiceError(werr) &&
                            Ask("Служба WARP остановлена", "Без неё Cloudflare WARP не подключается. Запустить службу? Windows спросит права администратора.", "Запустить", "Отмена"))
                        {
                            SetMsg(it, "запускаю службу WARP…", false);
                            bool started = false;
                            try { started = await Task.Run(() => WarpCtl.StartService()); }
                            catch (System.ComponentModel.Win32Exception) { werr = "отменено — без прав администратора службу не запустить"; }
                            if (started)
                            {
                                Journal.Write("warp", "служба запущена");
                                await Task.Delay(1500);
                                werr = null;
                                try { await Task.Run(() => WarpCtl.Run("connect")); }
                                catch (Exception ex) { werr = ex.Message; }
                            }
                        }
                        if (werr != null) throw new Exception(werr);
                        ok = await WaitConnected(it, 15000);
                        break;
                    case "koala":
                        if (it.VpnServer > 0) await KoalaApplyServer(it, true);
                        await Task.Run(() => KoalaCtl.SetTun(true, 4000));
                        ok = await WaitConnected(it, 15000);
                        break;
                }
            }
            catch (Exception ex) { err = ex.Message; }
            it.Busy = false;
            running = Core.RunningNames();
            SetMsg(it, ok ? null : "не подключился" + (err != null ? ": " + err : " — откройте " + title + " и проверьте сервер"), !ok);
            Journal.Write(it.Special, ok ? "подключён" + ServerNote(it) : "не подключился" + (err != null ? ": " + err : ""));
            RefreshState();
            return ok;
        }

        async Task<bool> WaitConnected(Item it, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (await ConnectedNow(it)) return true;
                await Task.Delay(600);
            }
            return await ConnectedNow(it);
        }

        async Task VpnDisconnect(Item it)
        {
            it.Busy = true;
            SetMsg(it, "отключаю…", false);
            bool off = false;
            string err = null;
            try
            {
                switch (it.Special)
                {
                    case "happ": off = await HappCtl.Disconnect(); break;
                    case "incy":
                        IncyCtl.Send(it, "disconnect");
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 10000 && await ConnectedNow(it)) await Task.Delay(600);
                        off = !await ConnectedNow(it);
                        break;
                    case "warp":
                        await Task.Run(() => WarpCtl.Run("disconnect"));
                        off = await HappCtl.WaitFor(() => !WarpCtl.Connected(), 10000);
                        break;
                    case "koala":
                        await Task.Run(() => KoalaCtl.SetTun(false, 4000));
                        off = !await ConnectedNow(it);
                        break;
                }
            }
            catch (Exception ex) { err = ex.Message; }
            it.Busy = false;
            SetMsg(it, off ? null : "не отключился" + (err != null ? ": " + err : ""), !off);
            Journal.Write(it.Special, off ? "отключён" : "не отключился" + (err != null ? ": " + err : ""));
            RefreshState();
        }

        string ServerNote(Item it)
        {
            if (it.Special == "happ") return " (" + (HappCtl.CurrentServerName() ?? "?") + ")";
            if (it.Special == "koala" && vpn.Koala != null) return " (" + vpn.Koala.Now + ")";
            return "";
        }

        // Koala: выбрать сервер по номеру строки (1 — первая строка списка в Koala).
        async Task KoalaApplyServer(Item it, bool quietErrors)
        {
            try
            {
                KoalaState st = await Task.Run(() => KoalaCtl.GetState(3000));
                if (it.VpnServer < 1 || it.VpnServer > st.Servers.Count)
                {
                    if (!quietErrors) Status("В Koala нет строки №" + it.VpnServer + " — серверов в списке " + st.Servers.Count + ".");
                    return;
                }
                string name = st.Servers[it.VpnServer - 1];
                if (name != st.Now) await Task.Run(() => KoalaCtl.Select(st.Group, name, 3000));
                vpn.Koala = await Task.Run(() => { try { return KoalaCtl.GetState(2000); } catch { return st; } });
                Journal.Write("koala", "сервер №" + it.VpnServer + ": " + name);
            }
            catch (Exception ex)
            {
                Journal.Write("koala", "не удалось выбрать сервер: " + ex.Message);
                if (!quietErrors) Status("Не удалось выбрать сервер в Koala: " + ex.Message);
            }
        }

        // Кнопка «Сервер» на карточке любого VPN.
        void ShowVpnServers(Item it, Point? at = null)
        {
            if (it.Special == "koala") { ShowKoalaServers(it, at); return; }
            ContextMenuStrip m = Menus.New();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            if (it.Special == "happ")
            {
                var keep = new ToolStripMenuItem("Как выбрано в самом Happ") { Checked = it.FindPreset() == null, Padding = new Padding(2, 3, 2, 3) };
                keep.Click += delegate { it.HappServer = ""; SaveCfg(); cards.Invalidate(); Status("Happ будет подключаться к серверу, выбранному в нём самом."); };
                m.Items.Add(keep);
                m.Items.Add(new ToolStripSeparator());
                int slot = 0;
                foreach (HappPreset p in it.HappPresets.ToList())
                {
                    HappPreset pp = p;
                    bool now = string.Equals(happGuid ?? "", p.Guid, StringComparison.OrdinalIgnoreCase);
                    var mi = new ToolStripMenuItem((++slot) + ".  " + p.Name + (now ? "   ← сейчас" : ""))
                    { Checked = it.HappServer == p.Name, Padding = new Padding(2, 3, 2, 3) };
                    mi.Click += async delegate
                    {
                        if (Core.IsRunning(it, Core.RunningNames()) && !now) { await SwitchHappServer(it, pp); return; }
                        it.HappServer = pp.Name;
                        SaveCfg();
                        cards.Invalidate();
                        Status("Happ будет подключаться к «" + pp.Name + "» при запуске из Старт-пака.");
                    };
                    m.Items.Add(mi);
                }
                if (slot == 0)
                    m.Items.Add(new ToolStripMenuItem("Сохранённых серверов пока нет: подключитесь в Happ к нужному и нажмите «Запомнить»") { Enabled = false });
                m.Items.Add(new ToolStripSeparator());
                var cap = new ToolStripMenuItem("Запомнить сервер, выбранный в Happ" + (string.IsNullOrEmpty(happServerName) ? "" : " («" + happServerName + "»)")) { Padding = new Padding(2, 3, 2, 3) };
                cap.Click += delegate { CaptureHappServer(it); };
                m.Items.Add(cap);
            }
            else if (it.Special == "warp")
            {
                m.Items.Add(new ToolStripMenuItem("WARP сам подключается к ближайшему серверу Cloudflare") { Checked = true, Enabled = false });
                m.Items.Add(new ToolStripMenuItem("Выбирать сервер у WARP нельзя") { Enabled = false });
            }
            else
            {
                m.Items.Add(new ToolStripMenuItem("INCY подключается к серверу, выбранному в нём самом") { Checked = true, Enabled = false });
                m.Items.Add(new ToolStripMenuItem("Выбирать сервер извне INCY не позволяет") { Enabled = false });
                m.Items.Add(new ToolStripSeparator());
                var open = new ToolStripMenuItem("Открыть INCY, чтобы выбрать сервер") { Padding = new Padding(2, 3, 2, 3) };
                open.Click += delegate { try { Core.ShellOpen(Core.Expand(it.Path)); } catch (Exception ex) { Status("Не удалось открыть INCY: " + ex.Message); } };   // второй запуск просто покажет окно INCY
                m.Items.Add(open);
            }
            lastMenu = m;
            m.Show(cards, at ?? cards.PointToClient(Cursor.Position));
        }

        void ShowKoalaServers(Item it, Point? at = null)
        {
            KoalaState st = vpn.Koala;
            if (st == null && KoalaCtl.AppRunning(Core.RunningNames()))
                try { st = KoalaCtl.GetState(1500); vpn.Koala = st; } catch { }
            ContextMenuStrip m = Menus.New();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            var keep = new ToolStripMenuItem("Как выбрано в самой Koala") { Checked = it.VpnServer == 0, Padding = new Padding(2, 3, 2, 3) };
            keep.Click += delegate { it.VpnServer = 0; SaveCfg(); cards.Invalidate(); };
            m.Items.Add(keep);
            m.Items.Add(new ToolStripSeparator());
            if (st == null)
            {
                m.Items.Add(new ToolStripMenuItem("Откройте Koala Clash — тогда появится список серверов") { Enabled = false });
            }
            else
            {
                int shown = Math.Min(30, st.Servers.Count);
                for (int i = 0; i < shown; i++)
                {
                    int n = i + 1;
                    string name = st.Servers[i];
                    var mi = new ToolStripMenuItem(n + ".  " + name + (name == st.Now ? "   ← сейчас" : ""))
                    { Checked = it.VpnServer == n, Padding = new Padding(2, 3, 2, 3) };
                    mi.Click += async delegate
                    {
                        it.VpnServer = n;
                        SaveCfg();
                        await KoalaApplyServer(it, false);
                        cards.Invalidate();
                    };
                    m.Items.Add(mi);
                }
                if (st.Servers.Count > shown)
                    m.Items.Add(new ToolStripMenuItem("…ещё " + (st.Servers.Count - shown) + " — выберите их в самой Koala") { Enabled = false });
            }
            lastMenu = m;
            m.Show(cards, at ?? cards.PointToClient(Cursor.Position));
        }

        // Строки для карточки VPN.
        void DescribeVpn(Item it, CardInfo ci, List<string> detail, bool run, bool conn)
        {
            if (demo) { detail.Add(it.HappAutoConnect ? "автоподключение" : "без автоподключения"); return; }
            if (it.Special == "happ")
            {
                HappPreset pr = it.FindPreset();
                if (pr != null)
                {
                    bool same = string.Equals(happGuid ?? "", pr.Guid, StringComparison.OrdinalIgnoreCase);
                    int n = it.HappPresets.IndexOf(pr) + 1;
                    detail.Add("слот " + n + ": " + (same ? pr.Name : pr.Name + " (в Happ сейчас: " + (happServerName ?? "?") + ")"));
                }
                else if (!string.IsNullOrEmpty(happServerName)) detail.Add(happServerName);
            }
            if (it.Special == "koala")
            {
                if (vpn.Koala != null && vpn.Koala.Now.Length > 0)
                    detail.Add((vpn.Koala.NowIndex > 0 ? "№" + vpn.Koala.NowIndex + " " : "") + vpn.Koala.Now);
            }
            if (!it.Busy && Core.PathExists(it.Path) && it.Special != "warp")
            {
                string label = "Сервер";
                if (it.Special == "koala" && it.VpnServer > 0) label = "Сервер: №" + it.VpnServer;
                if (it.Special == "happ" && it.FindPreset() != null) label = "Сервер: слот " + (it.HappPresets.IndexOf(it.FindPreset()) + 1);
                if (it.Special == "incy") label = "Сервер: как в INCY";
                ci.Chips.Add(new KeyValuePair<string, string>("vpnserver", label));
            }
            detail.Add(it.HappAutoConnect ? "автоподключение" : "без автоподключения");
            if (run && !it.Busy) ci.Chips.Add(conn ? new KeyValuePair<string, string>("disconnect", "Отключить") : new KeyValuePair<string, string>("connect", "Подключить"));
        }
    }
}
