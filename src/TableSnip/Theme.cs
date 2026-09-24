using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TableSnip.Interop;
using Windows.UI.ViewManagement;

namespace TableSnip;

/// <summary>
/// The TableSnip palette from design/design.md: amber "learning bits" on paper (light) or ink
/// (dark), warm greys for everything structural. Follows the Windows light/dark setting.
/// Set TABLESNIP_THEME=light|dark to force one (handy for screenshots).
/// </summary>
public static class Theme
{
    private static UISettings? _settings;

    public static bool IsDark { get; private set; }

    /// <summary>Raised after the palette has been re-applied because Windows switched modes.</summary>
    public static event Action? Changed;

    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;

        IsDark = DetectDark();
        var r = app.Resources;

        if (IsDark)
        {
            Set(r, "Brush.Window", "#141414");      // ink
            Set(r, "Brush.Surface", "#1E1D1B");
            Set(r, "Brush.Border", "#33312D");
            Set(r, "Brush.Text", "#F7F5F0");        // paper
            Set(r, "Brush.TextMuted", "#A39F96");
            Set(r, "Brush.Hover", "#2A2926");
            Set(r, "Brush.Success", "#7CC47F");
            Set(r, "Brush.GridLine", "#2E2C28");
            Set(r, "Brush.HeaderBg", "#262421");
            Set(r, "Brush.Accent", "#E0A03C");      // amber, lifted for contrast on ink
            Set(r, "Brush.AccentHover", "#EBB25A");
            Set(r, "Brush.OnAccent", "#141414");
            Set(r, "Brush.MarkKnown", "#4A4843");   // graphite: the family's "known bits" on dark
            Set(r, "Brush.AccentSoft", "#30E0A03C");
            Set(r, "Brush.Selection", "#48E0A03C");
            Set(r, "Brush.Busy", "#A0141414");
        }
        else
        {
            Set(r, "Brush.Window", "#F7F5F0");      // paper
            Set(r, "Brush.Surface", "#FFFFFF");
            Set(r, "Brush.Border", "#DAD6CD");
            Set(r, "Brush.Text", "#141414");        // ink
            Set(r, "Brush.TextMuted", "#6F6A60");
            Set(r, "Brush.Hover", "#EFECE5");
            Set(r, "Brush.Success", "#3B7D3A");
            Set(r, "Brush.GridLine", "#E9E5DC");
            Set(r, "Brush.HeaderBg", "#F3F0E9");
            Set(r, "Brush.Accent", "#B5730F");      // amber: the product colour
            Set(r, "Brush.AccentHover", "#9A6109");
            Set(r, "Brush.OnAccent", "#141414");    // ink on amber reads at 5:1; paper would not
            Set(r, "Brush.MarkKnown", "#C9C5BC");   // warm grey: the family's "known bits"
            Set(r, "Brush.AccentSoft", "#1CB5730F");
            Set(r, "Brush.Selection", "#30B5730F");
            Set(r, "Brush.Busy", "#A0F7F5F0");
        }
    }

    private static bool DetectDark()
    {
        var forced = Environment.GetEnvironmentVariable("TABLESNIP_THEME");
        if (string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(forced, "light", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            _settings ??= new UISettings();
            var bg = _settings.GetColorValue(UIColorType.Background);
            return bg.R < 128;
        }
        catch
        {
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return Convert.ToInt32(v ?? 1) == 0;
            }
            catch
            {
                return false;
            }
        }
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
                    Changed?.Invoke();
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

    private static void Set(ResourceDictionary r, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        r[key] = brush;
    }
}
