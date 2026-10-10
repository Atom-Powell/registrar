using System.Globalization;

namespace Registrar.Core.Config;

public sealed record ContentLint
{
    public required IReadOnlyList<string> ApprovedGlyphAllowlist { get; init; }

    // Nullable, not required: ContentLint is constructed directly in some unit tests
    // (CharacterLintTests) that only need the allowlist. TokenResidueLayer (QA-ENG-07)
    // fails closed with a ConfigurationError check when this is absent.
    public TokenResidueConfig? TokenResidue { get; init; }

    // Repairs for characters a chat surface substitutes in transit, keyed "U+XXXX".
    // Read by ImportParser.Normalize and only when the operator explicitly asks. Held as
    // the raw string map the config file declares; ParseTransportRepairs turns it into
    // code points. Nullable for the same reason TokenResidue is.
    public IReadOnlyDictionary<string, string>? TransportRepairs { get; init; }

    // Called once by ConfigLoader.Load, so a malformed key fails the config release where
    // every other config defect fails, rather than at whichever code path happens to read
    // the map first. ConfigBundle.TransportRepairs carries the result.
    //
    // Fails closed on a malformed key rather than skipping it. A skipped entry would mean
    // a character silently stops being repairable, with nothing to notice it: the config
    // would look complete and behave as though the entry were absent.
    //
    // The literal "$comment" is the file's own annotation, matching the convention the
    // rest of style-rules.json uses, and is the one key skipped deliberately. The match is
    // exact rather than a "$" prefix: a typo such as "$2019" is a broken repair entry, not
    // a comment, and skipping it would be the silent skip this method exists to prevent.
    public IReadOnlyDictionary<int, string> ParseTransportRepairs()
    {
        var result = new Dictionary<int, string>();
        if (TransportRepairs is null) return result;

        foreach (var (key, value) in TransportRepairs)
        {
            if (key == "$comment") continue;

            if (!key.StartsWith("U+", StringComparison.Ordinal)
                || !int.TryParse(
                        key.AsSpan(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var codePoint))
            {
                throw new InvalidOperationException(
                    $"contentLint.transportRepairs key '{key}' is not a U+XXXX code point.");
            }

            result[codePoint] = value;
        }

        return result;
    }
}

public sealed record TokenResidueConfig
{
    public required string Pattern { get; init; }
}
