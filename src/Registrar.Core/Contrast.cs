using System.Globalization;

namespace Registrar.Core;

// WCAG 2.1 contrast measurement. This lives in the engine rather than in a test so the
// accessibility guard and any future tooling share one implementation: two copies of a
// formula are two chances to get it subtly wrong and never notice.
public static class Contrast
{
    public static double Ratio(string hexA, string hexB)
    {
        var a = RelativeLuminance(hexA);
        var b = RelativeLuminance(hexB);
        var brighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (brighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(double value)
        => value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static (double R, double G, double B) Parse(string hex)
    {
        var text = hex.StartsWith('#') ? hex[1..] : hex;

        if (text.Length != 6)
            throw new ArgumentException($"'{hex}' is not a six-digit hex color.", nameof(hex));

        return (Component(text, 0), Component(text, 2), Component(text, 4));
    }

    private static double Component(string text, int offset)
        => int.Parse(text.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
}
