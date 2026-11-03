using System.Drawing;

namespace QuadExplorer;

// Builds a whole color theme from two chosen colors. Lightness, chroma and hue are worked in OKLCH, where equal
// steps look equally large; contrast is checked on the final 8-bit colors with the WCAG ratio. The chosen colors
// are moved as needed: backgrounds stay calm and light or dark enough for text, and text, secondary text and the
// accent are darkened or lightened until they are readable on every background they are drawn on.
internal static class PaletteGenerator
{
    internal sealed record ThemeColors(Color Canvas, Color Surface, Color Border, Color Text, Color Muted, Color Accent,
        Color Selected, Color ErrorText, Color Hover, Color Pressed, bool Dark);

    // Text against window, content, hover and selection backgrounds, and the weakest on a pressed button.
    internal const double TextContrast = 7, MutedContrast = 4.5, AccentContrast = 4.5, ErrorContrast = 4.5, PressedContrast = 4.5;

    private readonly record struct Lch(double L, double C, double H);

    public static ThemeColors Generate(Color accentSeed, Color backgroundSeed)
    {
        accentSeed = Color.FromArgb(accentSeed.R, accentSeed.G, accentSeed.B);
        backgroundSeed = Color.FromArgb(backgroundSeed.R, backgroundSeed.G, backgroundSeed.B);
        // Dark when white text would read better on the chosen background than black text.
        bool dark = Luminance(backgroundSeed) < Math.Sqrt(0.05 * 1.05) - 0.05;
        var background = ToLch(backgroundSeed);
        var accent = ToLch(accentSeed);
        // A grey background takes a faint tint of the accent so the two belong together.
        bool grey = background.C < 0.012;
        double hue = grey ? accent.H : background.H;
        // The preset-like lightness range and chroma ceiling keep even neon seeds out of large, vivid surfaces.
        double tint = grey ? Math.Min(accent.C, 0.15) * 0.06 : Math.Min(background.C, 0.03);
        double canvasL = dark ? Math.Clamp(background.L, 0.23, 0.32) : Math.Clamp(background.L, 0.925, 0.975);
        double surfaceL = dark ? canvasL + 0.035 : Math.Min(canvasL + 0.03, 1);
        var canvas = ToColor(new Lch(canvasL, tint, hue));
        var surface = ToColor(new Lch(surfaceL, tint * (dark ? 0.9 : 0.4), hue));
        var border = ToColor(new Lch(dark ? surfaceL + 0.12 : canvasL - 0.075, tint * 1.2, hue));
        // Hover, selection and pressed washes carry the accent's hue over the nearer background.
        double wash = Math.Min(accent.C, 0.14), baseL = dark ? surfaceL : Math.Min(canvasL, surfaceL), away = dark ? 1 : -1;
        var hover = ToColor(new Lch(baseL + away * 0.025, wash * 0.18, accent.H));
        var selected = ToColor(new Lch(baseL + away * (dark ? 0.09 : 0.045), wash * 0.32, accent.H));
        var pressed = ToColor(new Lch(baseL + away * (dark ? 0.14 : 0.085), wash * 0.42, accent.H));
        var text = Readable(new Lch(dark ? 0.93 : 0.27, Math.Min(tint * 2 + 0.01, 0.03), hue), dark,
            [(canvas, TextContrast), (surface, TextContrast), (hover, TextContrast), (selected, TextContrast), (pressed, PressedContrast)]);
        var muted = Readable(new Lch(dark ? 0.74 : 0.52, Math.Min(tint * 2 + 0.01, 0.03), hue), dark,
            [(canvas, MutedContrast), (surface, MutedContrast), (hover, MutedContrast), (selected, MutedContrast)]);
        // Mid-lightness accents retain their hue without becoming a neon link or a near-black/white spot.
        var accentColor = Readable(accent with { L = Math.Clamp(accent.L, dark ? 0.72 : 0.42, dark ? 0.82 : 0.58),
            C = Math.Min(accent.C, 0.18) }, dark,
            [(canvas, AccentContrast), (surface, AccentContrast), (hover, AccentContrast), (selected, AccentContrast)]);
        var error = Readable(new Lch(dark ? 0.75 : 0.52, dark ? 0.13 : 0.17, 27), dark,
            [(canvas, ErrorContrast), (surface, ErrorContrast)]);
        return new ThemeColors(canvas, surface, border, text, muted, accentColor, selected, error, hover, pressed, dark);
    }

    // Moves the color's lightness away from the backgrounds until it reaches every required contrast.
    // Black and white always qualify, because the generated backgrounds stay light or dark enough.
    private static Color Readable(Lch color, bool dark, ReadOnlySpan<(Color Back, double Ratio)> needs)
    {
        double limit = dark ? 0 : 1;
        foreach (var need in needs)
        {
            double back = Luminance(need.Back);
            double required = dark ? need.Ratio * (back + 0.05) - 0.05 : (back + 0.05) / need.Ratio - 0.05;
            limit = dark ? Math.Max(limit, required) : Math.Min(limit, required);
        }
        for (double l = color.L; ; l += dark ? 0.005 : -0.005)
        {
            double lightness = Math.Clamp(l, 0, 1);
            var candidate = ToColor(color with { L = lightness });
            double luminance = Luminance(candidate);
            if ((dark ? luminance >= limit : luminance <= limit) || (dark ? lightness == 1 : lightness == 0))
                return candidate;
        }
    }

    internal static double Luminance(Color color) =>
        0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);

    internal static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double ToLinear(int channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static int ToChannel(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        double value = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    // OKLab: https://bottosson.github.io/posts/oklab/
    private static Lch ToLch(Color color)
    {
        double r = ToLinear(color.R), g = ToLinear(color.G), b = ToLinear(color.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double lightness = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double hue = Math.Atan2(bb, a) * 180 / Math.PI;
        return new Lch(lightness, Math.Sqrt(a * a + bb * bb), hue < 0 ? hue + 360 : hue);
    }

    private static (double R, double G, double B) ToLinearRgb(double lightness, double chroma, double hue)
    {
        double a = chroma * Math.Cos(hue * Math.PI / 180), b = chroma * Math.Sin(hue * Math.PI / 180);
        double l = lightness + 0.3963377774 * a + 0.2158037573 * b;
        double m = lightness - 0.1055613458 * a - 0.0638541728 * b;
        double s = lightness - 0.0894841775 * a - 1.2914855480 * b;
        l = l * l * l; m = m * m * m; s = s * s * s;
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    // Colors outside sRGB keep their lightness and hue and lose chroma until they fit.
    private static Color ToColor(Lch color)
    {
        double lightness = Math.Clamp(color.L, 0, 1);
        var rgb = ToLinearRgb(lightness, color.C, color.H);
        if (!InGamut(rgb))
        {
            double low = 0, high = color.C;
            for (int i = 0; i < 24; i++)
            {
                double middle = (low + high) / 2;
                if (InGamut(ToLinearRgb(lightness, middle, color.H))) low = middle;
                else high = middle;
            }
            rgb = ToLinearRgb(lightness, low, color.H);
        }
        return Color.FromArgb(ToChannel(rgb.R), ToChannel(rgb.G), ToChannel(rgb.B));
    }

    private static bool InGamut((double R, double G, double B) rgb) =>
        rgb.R is >= -1e-4 and <= 1 + 1e-4 && rgb.G is >= -1e-4 and <= 1 + 1e-4 && rgb.B is >= -1e-4 and <= 1 + 1e-4;
}
