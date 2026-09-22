using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TableSnip.Interop;
using Windows.UI.ViewManagement;

namespace TableSnip;

/// <summary>Follows the Windows light/dark setting and accent colour.</summary>
public static class Theme
{
    private static UISettings? _settings;
    public static bool IsDark { get; private set; }

    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;

        bool dark = false;
        Color accent = Color.FromRgb(0x0F, 0x6C, 0xBD);
        Color accentText = Colors.White;
        Color accentHover = Color.FromRgb(0x11, 0x5E, 0xA3);

        try
        {
            _settings ??= new UISettings();
            var bg = _settings.GetColorValue(UIColorType.Background);
            dark = bg.R < 128;
            var a = _settings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.Accent);
            var ah = _settings.GetColorValue(dark ? UIColorType.AccentLight1 : UIColorType.AccentDark1);
            accent = Color.FromRgb(a.R, a.G, a.B);
            accentHover = Color.FromRgb(ah.R, ah.G, ah.B);
            accentText = dark ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;
        }
        catch
        {
            // Fall back to the defaults above.
        }

        IsDark = dark;
        var r = app.Resources;
        if (dark)
        {
            Set(r, "Brush.Window", 0x20, 0x20, 0x24);
            Set(r, "Brush.Surface", 0x2B, 0x2B, 0x30);
            Set(r, "Brush.Border", 0x3E, 0x3E, 0x45);
            Set(r, "Brush.Text", 0xF2, 0xF2, 0xF4);
            Set(r, "Brush.TextMuted", 0xA6, 0xA6, 0xAE);
            Set(r, "Brush.Hover", 0x36, 0x36, 0x3D);
            Set(r, "Brush.Success", 0x6C, 0xCB, 0x8A);
            Set(r, "Brush.GridLine", 0x3A, 0x3A, 0x41);
            Set(r, "Brush.HeaderBg", 0x33, 0x33, 0x39);
        }
        else
        {
            Set(r, "Brush.Window", 0xF7, 0xF7, 0xF8);
            Set(r, "Brush.Surface", 0xFF, 0xFF, 0xFF);
            Set(r, "Brush.Border", 0xE3, 0xE3, 0xE6);
            Set(r, "Brush.Text", 0x1B, 0x1B, 0x1F);
            Set(r, "Brush.TextMuted", 0x6E, 0x6E, 0x76);
            Set(r, "Brush.Hover", 0xEC, 0xEC, 0xEF);
            Set(r, "Brush.Success", 0x0E, 0x7A, 0x3B);
            Set(r, "Brush.GridLine", 0xEA, 0xEA, 0xEE);
            Set(r, "Brush.HeaderBg", 0xF3, 0xF3, 0xF5);
        }
        r["Brush.Accent"] = Frozen(new SolidColorBrush(accent));
        r["Brush.AccentHover"] = Frozen(new SolidColorBrush(accentHover));
        r["Brush.OnAccent"] = Frozen(new SolidColorBrush(accentText));
        r["Brush.AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x30 : (byte)0x18, accent.R, accent.G, accent.B)));
        r["Brush.Selection"] = Frozen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x50 : (byte)0x28, accent.R, accent.G, accent.B)));
        r["Brush.Busy"] = Frozen(new SolidColorBrush(Color.FromArgb(0xA0, dark ? (byte)0x20 : (byte)0xFF, dark ? (byte)0x20 : (byte)0xFF, dark ? (byte)0x24 : (byte)0xFF)));
    }

    /// <summary>Re-apply automatically when the user flips Windows between light and dark.</summary>
    public static void StartListening()
    {
        try
        {
            _settings ??= new UISettings();
            _settings.ColorValuesChanged += (_, _) =>
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    Apply();
                    foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
                });
        }
        catch
        {
        }
    }

    /// <summary>Dark title bar to match the content (Windows 10 20H1+).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
    }

    private static void Set(ResourceDictionary r, string key, byte red, byte green, byte blue) =>
        r[key] = Frozen(new SolidColorBrush(Color.FromRgb(red, green, blue)));

    private static SolidColorBrush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
