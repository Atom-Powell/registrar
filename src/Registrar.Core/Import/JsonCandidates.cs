using System.Text.RegularExpressions;

namespace Registrar.Core.Import;

// A JSON-shaped span pulled out of a chat reply, together with where it came from.
// Provenance is load bearing: a fenced block is by construction an attempt at JSON, while
// the fallback span is anchored on the first brace anywhere in the raw text and so can be
// ordinary prose that merely mentions one. A caller that wants to be suspicious of the
// second must be able to tell it from the first.
public sealed record JsonCandidate(string Text, bool Fenced);

// Finds JSON-shaped spans in a chat reply. It knows nothing about AGRs: its whole job is
// to undo what a chat surface wraps around a payload. Kept separate from ImportParser so
// the recovery heuristics can be tested against raw text alone.
public static class JsonCandidates
{
    // Non-greedy so consecutive fences are separate matches rather than one span running
    // from the first opening fence to the last closing one.
    private static readonly Regex Fenced = new(
        "```[a-zA-Z0-9]*[ \\t]*\\r?\\n(.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Ordered longest first. A reply sometimes fences a short illustrative fragment beside
    // the full envelope, and the envelope is the larger one, so length is the tiebreak
    // rather than document order or fence tag.
    public static IReadOnlyList<JsonCandidate> Collect(string pastedText)
    {
        var candidates = new List<JsonCandidate>();

        foreach (Match match in Fenced.Matches(pastedText))
        {
            var body = match.Groups[1].Value;
            if (body.Contains('{')) candidates.Add(new JsonCandidate(body, Fenced: true));
        }

        var span = BraceSpan(pastedText);
        if (span is not null) candidates.Add(new JsonCandidate(span, Fenced: false));

        return candidates
            .Select(c => c with { Text = c.Text.Trim() })
            .Where(c => c.Text.Length > 0)
            // Deduplicates by text, keeping the fenced provenance whenever the same span
            // was found both ways. A reply whose only content is one fenced envelope is
            // also its own outermost brace span, so the two collect to the same text; a
            // caller that treats the fallback more suspiciously must not lose the fact
            // that a fence held it.
            .GroupBy(c => c.Text, StringComparer.Ordinal)
            .Select(g => new JsonCandidate(g.Key, g.Any(c => c.Fenced)))
            .OrderByDescending(c => c.Text.Length)
            .ToList();
    }

    // True when braces or brackets never close, which is the signature of a reply cut off
    // by a length limit rather than one that is merely malformed.
    public static bool LooksTruncated(string candidate)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var ch in candidate)
        {
            if (escaped) { escaped = false; continue; }
            if (inString)
            {
                if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"': inString = true; break;
                case '{':
                case '[': depth++; break;
                case '}':
                case ']': depth--; break;
            }
        }

        return depth > 0 || inString;
    }

    // The outermost brace span of the raw text: from the first '{' to its matching '}', or
    // to the end when it never closes, so a truncated reply is still a candidate that can
    // be reported as truncated rather than vanishing.
    private static string? BraceSpan(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped) { escaped = false; continue; }
            if (inString)
            {
                if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                    break;
            }
        }

        return text[start..];
    }
}
