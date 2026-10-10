using System.Text;

namespace Registrar.Core;

public sealed record Offense(Rune Codepoint, int Position);

public static class CharacterLint
{
    private const int MaxShown = 5;

    // Friendly labels for the codepoints an operator is most likely to hit. This
    // map is engine design (message ergonomics), not framework policy; unlabeled
    // codepoints degrade to the bare U+XXXX form.
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [0x2014] = "em dash",
        [0x2013] = "en dash",
        [0x2026] = "ellipsis",
        [0x00A0] = "no-break space",
        [0x00AD] = "soft hyphen",
        [0x2018] = "left single quote",
        [0x2019] = "right single quote",
        [0x201C] = "left double quote",
        [0x201D] = "right double quote"
    };

    public static IReadOnlyList<Offense> Scan(string text, CharacterPolicy policy)
    {
        var offenses = new List<Offense>();
        int position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            position++;
            if (!policy.IsAllowed(rune)) offenses.Add(new Offense(rune, position));
        }
        return offenses;
    }

    public static string Describe(Rune rune)
    {
        var code = $"U+{rune.Value:X4}";
        return Labels.TryGetValue(rune.Value, out var label) ? $"{code} ({label})" : code;
    }

    public static string FormatActual(IReadOnlyList<Offense> offenses)
    {
        var shown = offenses.Take(MaxShown)
            .Select(o => $"{Describe(o.Codepoint)} at position {o.Position}");
        var joined = string.Join("; ", shown);
        return offenses.Count > MaxShown
            ? $"{joined}; and {offenses.Count - MaxShown} more"
            : joined;
    }
}
