// Значки программ: из exe/ярлыка, приложений Store, плитки для .bat. Значок трея с цветной точкой.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace StartPack
{
    public static class IconLoader
    {
        public static Image For(Item it, int size)
        {
            Icon ico = null;
            try
            {
                string p = Core.Expand(it.Path);
                string ext = (Path.GetExtension(p) ?? "").ToLowerInvariant();
                // У .bat нет своего значка — рисуем плитку: щит для zapret, «консоль» для остальных.
                if (it.UpdateKind == "zapret") return Tile("", Color.FromArgb(14, 165, 233), Color.FromArgb(99, 102, 241), size);
                if (ext == ".bat" || ext == ".cmd" || ext == ".ps1") return Tile("", Color.FromArgb(100, 116, 139), Color.FromArgb(51, 65, 85), size);
                if (Core.IsShell(p))
                {
                    Bitmap b = ShellIcon.Get(p, size);
                    if (b != null) return Fit(b, size);
                }
                else if (Core.SafeFileName(p).Equals("Update.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Core.SafeDir(p) ?? "", "app.ico")))
                    ico = new Icon(Path.Combine(Core.SafeDir(p), "app.ico"), size, size);
                else if (File.Exists(p))
                {
                    string src = p;
                    if (ext == ".lnk")
                    {
                        string target = Core.ResolveLnk(p);
                        if (target.Length > 0 && File.Exists(target)) src = target;
                    }
                    Bitmap b = ShellIcon.Get(src, size);
                    if (b != null) return Fit(b, size);
                    ico = Icon.ExtractAssociatedIcon(p);
                }
            }
            catch { }
            if (ico == null)
            {
                // программа ещё не установлена — плитка с буквой вместо безликого значка
                Image letter = KnownLetter(it, size);
                if (letter != null) return letter;
                ico = SystemIcons.Application;
            }
            return Fit(ico.ToBitmap(), size);
        }

        static Image KnownLetter(Item it, int size)
        {
            switch (Installers.KindOf(it))
            {
                case "tgws": return Letter("T", Color.FromArgb(56, 189, 248), Color.FromArgb(14, 116, 204), size);
                case "telegram": return Letter("T", Color.FromArgb(42, 171, 238), Color.FromArgb(34, 140, 210), size);
                case "discord": return Letter("D", Color.FromArgb(114, 137, 255), Color.FromArgb(88, 101, 242), size);
                case "spotify": return Letter("S", Color.FromArgb(30, 215, 96), Color.FromArgb(22, 160, 72), size);
                case "happ": return Letter("H", Color.FromArgb(75, 85, 99), Color.FromArgb(31, 41, 55), size);
                case "incy": return Letter("I", Color.FromArgb(167, 139, 250), Color.FromArgb(109, 40, 217), size);
                case "koala": return Letter("K", Color.FromArgb(45, 212, 191), Color.FromArgb(15, 118, 110), size);
                case "warp": return Letter("W", Color.FromArgb(251, 146, 60), Color.FromArgb(234, 88, 12), size);
            }
            return null;
        }

        public static Image Letter(string ch, Color a, Color b, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var r = new RectangleF(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f);
                using (GraphicsPath path = Gfx.Round(r, size * 0.22f))
                using (var br = new LinearGradientBrush(r, a, b, 45f))
                    g.FillPath(br, path);
                using (var f = new Font("Segoe UI Semibold", size * 0.5f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(ch, f, Brushes.White, new RectangleF(0, size * 0.01f, size, size), sf);
            }
            return bmp;
        }

        public static Image ForPath(string p, int size)
        {
            try
            {
                Bitmap b = ShellIcon.Get(p, size);
                if (b != null) return Fit(b, size);
                if (File.Exists(p)) return Fit(Icon.ExtractAssociatedIcon(p).ToBitmap(), size);
            }
            catch { }
            return Fit(SystemIcons.Application.ToBitmap(), size);
        }

        public static Image Tile(string glyph, Color a, Color b, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var r = new RectangleF(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f);
                using (GraphicsPath path = Gfx.Round(r, size * 0.22f))
                using (var br = new LinearGradientBrush(r, a, b, 45f))
                    g.FillPath(br, path);
                using (var f = new Font(Fonts.Icons(10).FontFamily, size * 0.46f, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
            }
            return bmp;
        }

        public static Bitmap Fit(Bitmap src, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, size, size);
            }
            src.Dispose();
            return bmp;
        }

        // Логотип из встроенного PNG (256×256) нужного размера.
        public static Bitmap App(int size)
        {
            try
            {
                using (Stream s = typeof(IconLoader).Assembly.GetManifestResourceStream("StartPack.app-256.png"))
                using (var src = new Bitmap(s))
                {
                    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, size, size);
                    }
                    return bmp;
                }
            }
            catch { return new Bitmap(size, size); }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        // Значок трея: ракета + точка состояния (зелёная — всё работает, жёлтая — не всё, красная — сбой).
        public static Icon TrayIcon(int size, Color? dot, ref IntPtr handle)
        {
            using (Bitmap bmp = App(size))
            {
                if (dot.HasValue)
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        float d = size * 0.46f;
                        var r = new RectangleF(size - d - 0.5f, size - d - 0.5f, d, d);
                        using (var ring = new SolidBrush(Color.FromArgb(17, 19, 24))) g.FillEllipse(ring, RectangleF.Inflate(r, size * 0.06f, size * 0.06f));
                        using (var b = new SolidBrush(dot.Value)) g.FillEllipse(b, r);
                    }
                IntPtr h = bmp.GetHicon();
                if (handle != IntPtr.Zero) DestroyIcon(handle);
                handle = h;
                return Icon.FromHandle(h);
            }
        }
    }

    // Значок любого объекта оболочки (в т. ч. приложения из Store) в хорошем качестве.
    public static class ShellIcon
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        public static Bitmap Get(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            try
            {
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                // 0x1 BIGGERSIZEOK | 0x4 ICONONLY — значок, а не миниатюра содержимого
                if (f.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                using (Bitmap raw = Image.FromHbitmap(hbm))
                {
                    var rect = new Rectangle(0, 0, raw.Width, raw.Height);
                    BitmapData d = raw.LockBits(rect, ImageLockMode.ReadOnly, raw.PixelFormat);
                    try
                    {
                        using (var view = new Bitmap(d.Width, d.Height, d.Stride, PixelFormat.Format32bppPArgb, d.Scan0))
                            return new Bitmap(view);
                    }
                    finally { raw.UnlockBits(d); }
                }
            }
            catch { return null; }
            finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
        }
    }
}
