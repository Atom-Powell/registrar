using System.Text;
using Registrar.Core.Config;

namespace Registrar.Core;

// The allowed ranges are fixed by ORG-MST-SPEC-001 Section 4.2 and live in code;
// only the glyph allowlist is config-driven. Changing the ranges is an ADR-level
// decision, not a config release.
public sealed class CharacterPolicy
{
    private readonly HashSet<int> _allowedGlyphs;

    public CharacterPolicy(IReadOnlyList<string> approvedGlyphs)
    {
        _allowedGlyphs = new HashSet<int>();
        foreach (var glyph in approvedGlyphs)
            foreach (var rune in glyph.EnumerateRunes())
                _allowedGlyphs.Add(rune.Value);
    }

    public static CharacterPolicy FromConfig(ContentLint contentLint)
        => new(contentLint.ApprovedGlyphAllowlist);

    public bool IsAllowed(Rune rune)
    {
        int v = rune.Value;
        if (v >= 0x20 && v <= 0x7E) return true;                    // printable Basic Latin
        if (v >= 0x00A1 && v <= 0x00FF && v != 0x00AD) return true; // Latin-1 minus NBSP/soft hyphen
        return _allowedGlyphs.Contains(v);
    }
}
