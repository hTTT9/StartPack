// Точка входа и оформление.
//   startpack               — окно
//   startpack --autorun     — «Запустить всё» без окна (так его запускает автозапуск при входе в систему)
//   startpack --selftest F  — самопроверка без окна, результат — в файл F (для GitHub Actions)
//   startpack --screenshots D — снимки окон без экрана в папку D

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace StartPack.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : "selftest.log");
        if (args.Length > 0 && args[0] == "--screenshots") return Shots.Run(args.Length > 1 ? args[1] : "shots");
        if (args.Contains("--version")) { Console.WriteLine("Старт-пак для Linux " + App.Version); return 0; }
        if (args.Contains("--autorun")) return Autorun();

        if (!SingleInstance.Acquire()) return 0;   // уже открыт — показали его окно
        App.Updated = args.Contains("--updated");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    // При входе в систему: всё из «Запустить всё», без окна. Пароль для zapret спросит системное окно.
    static int Autorun()
    {
        var cfg = Store.Load();
        var e = new Engine(cfg);
        Log.Write("старт", "автозапуск при входе в систему");
        Thread.Sleep(5000);   // дать сети подняться
        e.RunAll().GetAwaiter().GetResult();
        return 0;
    }
}

// Второй запуск не открывает второе окно, а просит первое показаться.
public static class SingleInstance
{
    static FileStream lockFile;
    static string Dir => Path.Combine(Path.GetTempPath(), "startpack-" + Environment.UserName);
    public static string ShowRequest => Path.Combine(Dir, "show");

    public static bool Acquire()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            lockFile = new FileStream(Path.Combine(Dir, "lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            File.WriteAllText(ShowRequest, DateTime.Now.ToString("O"));
            return false;
        }
    }
}

public class App : Application
{
    public const string Version = "1.0.0";
    public static bool Updated;
    public static Config Cfg;
    public static Engine Engine;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Cfg ??= Store.Load();
        Pal.Apply(this, Cfg.Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            Engine ??= new Engine(Cfg);
            d.MainWindow = new MainWindow(Engine);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

// Цвета — как у Старт-пака для Windows: тёмная и мягкая светлая тема.
public static class Pal
{
    public static bool Dark = true;
    public static IBrush Bg, Surface, SurfaceHover, SurfaceAlt, Border, Text, TextDim, TextFaint, Accent, AccentHover, OnAccent, Green, Red, Amber, Blue, Track;

    static IBrush B(uint rgb) => new SolidColorBrush(Color.FromUInt32(0xFF000000 | rgb));

    public static void Apply(Application app, string mode)
    {
        Dark = mode == "light" ? false : mode == "system" ? (app.PlatformSettings?.GetColorValues().ThemeVariant != Avalonia.Platform.PlatformThemeVariant.Light) : true;
        if (Dark)
        {
            Bg = B(0x111318); Surface = B(0x1A1D24); SurfaceHover = B(0x21252E); SurfaceAlt = B(0x2A2F3A); Border = B(0x262A33);
            Text = B(0xE8EAED); TextDim = B(0x9AA3AF); TextFaint = B(0x6B7280);
            Accent = B(0x34D399); AccentHover = B(0x6EE7B7); OnAccent = B(0x052E1C);
            Green = B(0x34D399); Red = B(0xF87171); Amber = B(0xFBBF24); Blue = B(0x60A5FA); Track = B(0x3A404C);
        }
        else
        {
            Bg = B(0xE2E5EA); Surface = B(0xECEEF2); SurfaceHover = B(0xE6E9EE); SurfaceAlt = B(0xD9DDE3); Border = B(0xD0D5DC);
            Text = B(0x1F2328); TextDim = B(0x4B5563); TextFaint = B(0x7A8290);
            Accent = B(0x15803D); AccentHover = B(0x166534); OnAccent = B(0xFFFFFF);
            Green = B(0x15803D); Red = B(0xB91C1C); Amber = B(0xB45309); Blue = B(0x2563EB); Track = B(0xBCC3CC);
        }
        app.RequestedThemeVariant = Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var accent = ((SolidColorBrush)Accent).Color;
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = ((SolidColorBrush)AccentHover).Color;
        app.Resources["SystemAccentColorDark1"] = accent;
        app.Resources["ToggleSwitchFillOn"] = Accent;
        app.Resources["ToggleSwitchFillOnPointerOver"] = AccentHover;
        app.Resources["ToggleSwitchKnobFillOn"] = OnAccent;
        app.Resources["ToggleSwitchKnobFillOnPointerOver"] = OnAccent;
    }
}
