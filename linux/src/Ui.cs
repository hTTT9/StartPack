// Мелкие строительные блоки окна: кнопки в цветах темы, надписи, плитки-значки, окно-вопрос.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace StartPack.Linux;

public enum Kind { Primary, Secondary, Ghost, Danger, Start, Stop }

public static class Ui
{
    public static TextBlock Text(string s, double size = 14, IBrush color = null, FontWeight weight = FontWeight.Normal, bool wrap = false) => new()
    {
        Text = s, FontSize = size, Foreground = color ?? Pal.Text, FontWeight = weight,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    // Кнопка: цвета задаются её собственными ресурсами, поэтому и наведение, и нажатие — в цветах темы.
    public static Button Btn(string text, Kind kind = Kind.Secondary, string icon = null, double size = 13.5)
    {
        IBrush bg, hover, fg;
        switch (kind)
        {
            case Kind.Primary: bg = Pal.Accent; hover = Pal.AccentHover; fg = Pal.OnAccent; break;
            case Kind.Ghost: bg = Brushes.Transparent; hover = Pal.SurfaceAlt; fg = Pal.TextDim; break;
            case Kind.Danger: bg = Pal.SurfaceAlt; hover = Mix(Pal.SurfaceAlt, Pal.Red, 0.3); fg = Pal.Red; break;
            case Kind.Start: bg = Mix(Pal.SurfaceAlt, Pal.Accent, 0.18); hover = Mix(Pal.SurfaceAlt, Pal.Accent, 0.32); fg = Pal.Accent; break;
            case Kind.Stop: bg = Pal.SurfaceAlt; hover = Mix(Pal.SurfaceAlt, Pal.Red, 0.28); fg = Pal.Text; break;
            default: bg = Pal.SurfaceAlt; hover = Mix(Pal.SurfaceAlt, Pal.Text, 0.08); fg = Pal.Text; break;
        }
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null)
        {
            if (icon == "stop")
                content.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = Pal.Red, VerticalAlignment = VerticalAlignment.Center });
            else
                content.Children.Add(new TextBlock { Text = icon, FontSize = size, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
        }
        if (!string.IsNullOrEmpty(text)) content.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0) };
        b.Resources["ButtonBackground"] = bg;
        b.Resources["ButtonBackgroundPointerOver"] = hover;
        b.Resources["ButtonBackgroundPressed"] = hover;
        b.Resources["ButtonBackgroundDisabled"] = Pal.SurfaceAlt;
        b.Resources["ButtonForeground"] = fg;
        b.Resources["ButtonForegroundPointerOver"] = fg;
        b.Resources["ButtonForegroundPressed"] = fg;
        b.Resources["ButtonForegroundDisabled"] = Pal.TextFaint;
        b.Resources["ButtonBorderBrush"] = Brushes.Transparent;
        b.Resources["ButtonBorderBrushPointerOver"] = Brushes.Transparent;
        b.Resources["ButtonBorderBrushPressed"] = Brushes.Transparent;
        return b;
    }

    public static IBrush Mix(IBrush a, IBrush b, double k)
    {
        Color x = ((SolidColorBrush)a).Color, y = ((SolidColorBrush)b).Color;
        byte M(byte u, byte v) => (byte)Math.Round(u + (v - u) * k);
        return new SolidColorBrush(Color.FromRgb(M(x.R, y.R), M(x.G, y.G), M(x.B, y.B)));
    }

    public static Ellipse Dot(IBrush c, double d = 8) => new() { Width = d, Height = d, Fill = c, VerticalAlignment = VerticalAlignment.Center };

    // Плитка с буквой — значок программы (у каждой свой цвет).
    public static Control Tile(string kind, string name, double size = 36)
    {
        (uint a, uint b, string letter) = kind switch
        {
            "zapret" => (0x0EA5E9u, 0x6366F1u, "Z"),
            "tgws" => (0x38BDF8u, 0x0E74CCu, "T"),
            "telegram" => (0x2AABEEu, 0x228CD2u, "T"),
            "discord" => (0x7289FFu, 0x5865F2u, "D"),
            "spotify" => (0x1ED760u, 0x16A048u, "S"),
            "happ" => (0x4B5563u, 0x1F2937u, "H"),
            "warp" => (0xFB923Cu, 0xEA580Cu, "W"),
            _ => (0x64748Bu, 0x334155u, string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant())
        };
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromUInt32(0xFF000000 | a), 0), new GradientStop(Color.FromUInt32(0xFF000000 | b), 1) }
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = grad,
            Child = new TextBlock { Text = letter, FontSize = size * 0.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    public static Window Dialog(string title, Control content, double width = 520) => new()
    {
        Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Pal.Bg, ShowInTaskbar = false,
        Content = new Border { Padding = new Thickness(26, 20), Child = content }
    };

    // Вопрос с двумя кнопками. true — первая.
    public static async Task<bool> Ask(Window owner, string title, string text, string yes, string no = "Отмена")
    {
        bool result = false;
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Text(title, 17, weight: FontWeight.SemiBold, wrap: true));
        panel.Children.Add(Text(text, 14, Pal.TextDim, wrap: true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Window w = Dialog(title, panel);
        if (no != null) { var n = Btn(no); n.Click += (_, _) => w.Close(); row.Children.Add(n); }
        var y = Btn(yes, Kind.Primary); y.Click += (_, _) => { result = true; w.Close(); }; row.Children.Add(y);
        panel.Children.Add(row);
        await w.ShowDialog(owner);
        return result;
    }

    public static Task Info(Window owner, string title, string text) => Ask(owner, title, text, "Понятно", null);
}
