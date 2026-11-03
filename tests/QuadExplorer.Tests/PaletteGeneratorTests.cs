using System.Drawing;
using QuadExplorer;

internal static class PaletteGeneratorTests
{
    internal static (string Name, Action Run)[] Cases =
    [
        ("generated palettes keep quantized text and semantic colors readable", Readability),
        ("generated palettes separate interaction states and text hierarchy", Hierarchy),
        ("generated palettes preserve chromatic accent hues", AccentHue),
        ("generated palettes are deterministic and choose readable light or dark mode", DeterminismAndMode),
        ("generated palettes reproduce readable background and accent seeds exactly", ReadableSeeds),
        ("generated palettes keep distinct background choices distinct", DistinctBackgrounds),
        ("generated palettes retain background hue and chroma on calmer content", BackgroundFidelity)
    ];

    private static readonly Lazy<(Color Accent, Color Background, PaletteGenerator.ThemeColors Theme)[]> Sweep = new(() =>
    {
        var seeds = Seeds().DistinctBy(color => color.ToArgb()).ToArray();
        return seeds.SelectMany(accent => seeds.Select(background =>
            (accent, background, PaletteGenerator.Generate(accent, background)))).ToArray();
    });

    private static void Readability()
    {
        foreach (var (accent, background, theme) in Sweep.Value)
        {
            Color[] reading = [theme.Canvas, theme.Surface, theme.Hover, theme.Selected];
            foreach (var back in reading)
            {
                Readable(theme.Text, back, 7, "text", accent, background);
                Readable(theme.Muted, back, 4.5, "muted", accent, background);
                Readable(theme.Accent, back, 4.5, "accent", accent, background);
            }
            Readable(theme.Text, theme.Pressed, 4.5, "pressed text", accent, background);
            Readable(theme.ErrorText, theme.Canvas, 4.5, "canvas error", accent, background);
            Readable(theme.ErrorText, theme.Surface, 4.5, "surface error", accent, background);
        }
    }

    private static void Hierarchy()
    {
        foreach (var (accent, background, theme) in Sweep.Value)
        {
            // White cannot have a lighter content surface; do not darken the chosen canvas to fake a gap.
            Color[] states = [theme.Canvas, theme.Surface, theme.Hover, theme.Selected, theme.Pressed];
            int expected = theme.Canvas.ToArgb() == Color.White.ToArgb() ? 4 : 5;
            Require(states.Select(color => color.ToArgb()).Distinct().Count() == expected,
                "Interaction states must be distinct, with separate content except on white", accent, background);
            double direction = theme.Dark ? 1 : -1;
            Require(direction * (Lightness(theme.Selected) - Lightness(theme.Hover)) > 0.015
                && direction * (Lightness(theme.Pressed) - Lightness(theme.Selected)) > 0.025,
                "Hover, selection and pressed must have visibly ordered lightness", accent, background);
            Require(Contrast(theme.Text, theme.Surface) > Contrast(theme.Muted, theme.Surface),
                "Primary text must have stronger contrast than muted text", accent, background);
        }
    }

    private static void AccentHue()
    {
        foreach (var (accent, background, theme) in Sweep.Value)
        {
            var seed = HueChroma(accent);
            if (seed.Chroma < 0.035) continue; // Near-neutral hue is unstable after 8-bit rounding.
            var generated = HueChroma(theme.Accent);
            double distance = Math.Abs(seed.Hue - generated.Hue);
            distance = Math.Min(distance, 360 - distance);
            Require(generated.Chroma > 0.015 && distance <= 5,
                $"Accent hue changed by {distance:F2} degrees", accent, background);
        }
    }

    private static void DeterminismAndMode()
    {
        foreach (var (accent, background, theme) in Sweep.Value)
        {
            Require(theme == PaletteGenerator.Generate(accent, background),
                "Repeated generation must return the same palette", accent, background);
            bool whiteReadsBetter = Contrast(Color.White, background) > Contrast(Color.Black, background);
            Require(theme.Dark == whiteReadsBetter,
                "Mode must follow which text polarity reads better on the background seed", accent, background);
        }
    }

    private static void ReadableSeeds()
    {
        var accent = Hex("626525");
        foreach (var hex in new[]
        {
            "87CEFA", "FFFFFF", "FEFEFE", "FFFFFE", "F6F7F9", "F7F3E8", "E6E5E1", "E6F2EA", "E8F1F8",
            "EEEAF7", "F8E9ED", "EEDFC4", "25282D", "182638", "1D3028", "302336", "000000"
        })
        {
            var background = Hex(hex);
            var theme = PaletteGenerator.Generate(accent, background);
            Require(theme.Canvas.ToArgb() == background.ToArgb(),
                "A background with room for readable states must survive exactly", accent, background);
        }
        foreach (var (seed, background, theme) in Sweep.Value)
        {
            Color[] backs = [theme.Canvas, theme.Surface, theme.Hover, theme.Selected];
            if (backs.All(back => Contrast(seed, back) >= PaletteGenerator.AccentContrast))
                Require(theme.Accent.ToArgb() == seed.ToArgb(),
                    "An already-readable accent must survive exactly", seed, background);
        }
        var light = Hex("F6F7F9");
        Require(PaletteGenerator.Generate(accent, light).Accent.ToArgb() == accent.ToArgb(),
            "The owner's readable olive accent must survive exactly", accent, light);
        foreach (var seed in new[] { Color.Black, Hex("000080") })
            Require(PaletteGenerator.Generate(seed, light).Accent.ToArgb() == seed.ToArgb(),
                "Dark readable accents must not be brightened to a preset clamp", seed, light);
    }

    private static void DistinctBackgrounds()
    {
        var seeds = new[] { "FFFFFF", "F6F7F9", "E6E5E1", "D0D0D0", "C0C0C0", "B0B0B0", "25282D", "182638" };
        var accent = Hex("7C8530");
        var canvases = seeds.Select(hex => PaletteGenerator.Generate(accent, Hex(hex)).Canvas.ToArgb()).ToArray();
        Require(canvases.Distinct().Count() == seeds.Length,
            "White, the default, Stone, greys and dark choices must not collapse to one canvas", accent, Hex(seeds[0]));
        var background = Hex("E6E5E1");
        Require(PaletteGenerator.Generate(Color.Blue, background).Canvas.ToArgb()
            == PaletteGenerator.Generate(Color.Red, background).Canvas.ToArgb(),
            "A readable neutral canvas must not take its color from the accent", accent, background);
    }

    private static void BackgroundFidelity()
    {
        var accent = Hex("0078D4");
        foreach (var hex in new[] { "87CEFA", "98FB98", "FFB6C1", "D8BFD8", "FFDAB9", "00FF00", "FFFF00", "00FFFF", "182638", "1D3028", "302336" })
        {
            var seed = Hex(hex);
            var theme = PaletteGenerator.Generate(accent, seed);
            var original = HueChroma(seed);
            var canvas = HueChroma(theme.Canvas);
            var surface = HueChroma(theme.Surface);
            double canvasHue = Math.Abs(original.Hue - canvas.Hue);
            double surfaceHue = Math.Abs(original.Hue - surface.Hue);
            Require(Math.Min(canvasHue, 360 - canvasHue) < 5 && canvas.Chroma >= original.Chroma * 0.95,
                "A readable chromatic canvas must retain its hue and nearly all its chroma", accent, seed);
            Require(Math.Min(surfaceHue, 360 - surfaceHue) < 5 && surface.Chroma >= original.Chroma * 0.35
                && surface.Chroma < canvas.Chroma && Lightness(theme.Surface) > Lightness(theme.Canvas),
                "Content must be lighter and calmer without losing the background hue", accent, seed);
        }
    }

    private static Color Hex(string hex) =>
        Color.FromArgb(Convert.ToInt32(hex, 16) | unchecked((int)0xFF000000));

    private static double Lightness(Color color)
    {
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        return 0.2104542553 * Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
            + 0.7936177850 * Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
            - 0.0040720468 * Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
    }

    private static IEnumerable<Color> Seeds()
    {
        // Sweep 24 hues at three independent lightness/chroma levels, then include neutral,
        // saturated, threshold-adjacent and actual dialog swatches in both seed positions.
        for (int hue = 0; hue < 360; hue += 15)
        {
            yield return Hsl(hue, 0.8, 0.16);
            yield return Hsl(hue, 1, 0.5);
            yield return Hsl(hue, 0.35, 0.85);
        }
        foreach (var hex in new[]
        {
            "000000", "FFFFFF", "FEFEFE", "FFFFFE", "F6F7F9", "626525", "87CEFA", "808080", "757575", "767676", "FFFF00", "FF0000", "00FF00", "0000FF",
            "0078D4", "008B8B", "278349", "7C8530", "D97818", "CC4545", "CA4D8C", "8F57B5", "5966C7", "64748B",
            "F7F3E8", "E6E5E1", "E6F2EA", "E8F1F8", "EEEAF7", "F8E9ED", "EEDFC4", "25282D", "182638", "1D3028", "302336"
        })
            yield return Color.FromArgb(Convert.ToInt32(hex, 16) | unchecked((int)0xFF000000));
    }

    private static Color Hsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - chroma / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    private static double Linear(byte channel) => channel / 255.0 <= 0.04045
        ? channel / 255.0 / 12.92 : Math.Pow((channel / 255.0 + 0.055) / 1.055, 2.4);

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static (double Hue, double Chroma) HueChroma(Color color)
    {
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double hue = Math.Atan2(bb, a) * 180 / Math.PI;
        return (hue < 0 ? hue + 360 : hue, Math.Sqrt(a * a + bb * bb));
    }

    private static void Readable(Color foreground, Color back, double minimum, string role, Color accent, Color background)
    {
        double ratio = Contrast(foreground, back);
        Require(ratio >= minimum, $"{role} contrast {ratio:F4} is below {minimum}", accent, background);
    }

    private static void Require(bool condition, string message, Color accent, Color background)
    {
        if (!condition)
            throw new InvalidOperationException($"{message}; seeds #{accent.R:X2}{accent.G:X2}{accent.B:X2} / #{background.R:X2}{background.G:X2}{background.B:X2}");
    }
}
