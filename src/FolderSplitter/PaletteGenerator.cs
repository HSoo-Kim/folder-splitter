using System.Drawing;

namespace QuadExplorer;

// Builds a whole color theme from two chosen colors. Lightness, chroma and hue are worked in OKLCH, where equal
// steps look equally large; contrast is checked on the final 8-bit colors with the WCAG ratio. The chosen colors
// are moved only as needed for readability. Backgrounds retain their own hue and chroma; text and accents
// move in lightness until their final, quantized colors are readable on every background they are drawn on.
internal static class PaletteGenerator
{
    internal sealed record ThemeColors(Color Canvas, Color Surface, Color Border, Color Text, Color Muted, Color Accent,
        Color Selected, Color ErrorText, Color Hover, Color Pressed, bool Dark);

    // Text against window, content, hover and selection backgrounds, and the weakest on a pressed button.
    internal const double TextContrast = 7, MutedContrast = 4.5, AccentContrast = 4.5, ErrorContrast = 4.5, PressedContrast = 4.5;

    private readonly record struct Lch(double L, double C, double H);

    // Mixing 45% white gives light content a quieter, visibly lighter version of its canvas.
    // A 0.004 L minimum avoids rounding near-white content back to the unchanged canvas.
    // Dark content rises by 0.045 L; the floor keeps a black canvas's content visibly separate.
    private const double LightSurfaceMix = 0.45, LightSurfaceStep = 0.004, DarkSurfaceStep = 0.045, DarkSurfaceFloor = 0.12;
    // Equal ordering in lightness makes the three states legible even with a neutral accent.
    // Increasing accent mixes preserve the underlying background rather than replacing it with a preset tint.
    private const double HoverStep = 0.025, SelectedStep = 0.055, PressedStep = 0.095;
    private const double HoverMix = 0.10, SelectedMix = 0.22, PressedMix = 0.34;

    private readonly record struct Backgrounds(Color Canvas, Color Surface, Color Hover, Color Selected, Color Pressed);

    public static ThemeColors Generate(Color accentSeed, Color backgroundSeed)
    {
        accentSeed = Color.FromArgb(accentSeed.R, accentSeed.G, accentSeed.B);
        backgroundSeed = Color.FromArgb(backgroundSeed.R, backgroundSeed.G, backgroundSeed.B);
        // Dark when white text would read better on the chosen background than black text.
        bool dark = Luminance(backgroundSeed) < Math.Sqrt(0.05 * 1.05) - 0.05;
        var accent = ToLch(accentSeed);
        var backgrounds = BackgroundsFor(backgroundSeed, accent, dark);
        if (!HasTextRoom(backgrounds, dark))
        {
            // Search only toward the readable extreme. Keeping the passing endpoint makes this a
            // minimum lightness projection on final RGB colors, not a preset-like L/C clamp.
            var seed = ToLch(backgroundSeed);
            double failing = seed.L, passing = dark ? 0 : 1;
            backgrounds = BackgroundsFor(dark ? Color.Black : Color.White, accent, dark);
            for (int i = 0; i < 28; i++)
            {
                double middle = (failing + passing) / 2;
                var candidate = BackgroundsFor(ToColor(seed with { L = middle }), accent, dark);
                if (HasTextRoom(candidate, dark))
                {
                    passing = middle;
                    backgrounds = candidate;
                }
                else failing = middle;
            }
        }
        var (canvas, surface, hover, selected, pressed) = backgrounds;
        var background = ToLch(canvas);
        var border = ToColor(background with { L = background.L + (dark ? 0.09 : -0.075) });
        var text = Readable(ToColor(new Lch(dark ? 0.93 : 0.27, 0, background.H)), dark,
            [(canvas, TextContrast), (surface, TextContrast), (hover, TextContrast), (selected, TextContrast), (pressed, PressedContrast)]);
        var muted = Readable(ToColor(new Lch(dark ? 0.74 : 0.52, Math.Min(background.C, 0.025), background.H)), dark,
            [(canvas, MutedContrast), (surface, MutedContrast), (hover, MutedContrast), (selected, MutedContrast)]);
        var accentColor = Readable(accentSeed, dark,
            [(canvas, AccentContrast), (surface, AccentContrast), (hover, AccentContrast), (selected, AccentContrast)]);
        var error = Readable(ToColor(new Lch(dark ? 0.75 : 0.52, dark ? 0.13 : 0.17, 27)), dark,
            [(canvas, ErrorContrast), (surface, ErrorContrast)]);
        return new ThemeColors(canvas, surface, border, text, muted, accentColor, selected, error, hover, pressed, dark);
    }

    private static Backgrounds BackgroundsFor(Color canvas, Lch accent, bool dark)
    {
        var background = ToLch(canvas);
        double mix = Math.Min(1, Math.Max(LightSurfaceMix, LightSurfaceStep / (1 - background.L)));
        var surface = ToColor(dark
            ? background with { L = Math.Max(background.L + DarkSurfaceStep, DarkSurfaceFloor), C = background.C * 0.9 }
            : background with { L = background.L + (1 - background.L) * mix, C = background.C * (1 - mix) });
        // States sit beyond the nearer background: canvas in light mode, content in dark mode.
        var nearer = dark ? ToLch(surface) : background;
        return new(canvas, surface,
            Wash(nearer, accent, dark, HoverStep, HoverMix),
            Wash(nearer, accent, dark, SelectedStep, SelectedMix),
            Wash(nearer, accent, dark, PressedStep, PressedMix));
    }

    private static Color Wash(Lch background, Lch accent, bool dark, double step, double mix)
    {
        double a = background.C * Math.Cos(background.H * Math.PI / 180) * (1 - mix)
            + accent.C * Math.Cos(accent.H * Math.PI / 180) * mix;
        double b = background.C * Math.Sin(background.H * Math.PI / 180) * (1 - mix)
            + accent.C * Math.Sin(accent.H * Math.PI / 180) * mix;
        return ToColor(new Lch(background.L + (dark ? step : -step), Math.Sqrt(a * a + b * b),
            Math.Atan2(b, a) * 180 / Math.PI));
    }

    private static bool HasTextRoom(Backgrounds backgrounds, bool dark)
    {
        var text = dark ? Color.White : Color.Black;
        return Contrast(text, backgrounds.Canvas) >= TextContrast
            && Contrast(text, backgrounds.Surface) >= TextContrast
            && Contrast(text, backgrounds.Hover) >= TextContrast
            && Contrast(text, backgrounds.Selected) >= TextContrast
            && Contrast(text, backgrounds.Pressed) >= PressedContrast;
    }

    // Preserve an already-readable seed exactly; otherwise retain its hue and as much chroma as fits.
    // Search the final 8-bit colors rather than stopping short of the contrast boundary in fixed L steps.
    private static Color Readable(Color seed, bool dark, ReadOnlySpan<(Color Back, double Ratio)> needs)
    {
        double limit = dark ? 0 : 1;
        foreach (var need in needs)
        {
            double back = Luminance(need.Back);
            double required = dark ? need.Ratio * (back + 0.05) - 0.05 : (back + 0.05) / need.Ratio - 0.05;
            limit = dark ? Math.Max(limit, required) : Math.Min(limit, required);
        }
        double luminance = Luminance(seed);
        if (dark ? luminance >= limit : luminance <= limit) return seed;
        var color = ToLch(seed);
        double failing = color.L, passing = dark ? 1 : 0;
        var result = dark ? Color.White : Color.Black;
        for (int i = 0; i < 28; i++)
        {
            double middle = (failing + passing) / 2;
            var candidate = ToColor(color with { L = middle });
            luminance = Luminance(candidate);
            if (dark ? luminance >= limit : luminance <= limit)
            {
                passing = middle;
                result = candidate;
            }
            else failing = middle;
        }
        return result;
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
