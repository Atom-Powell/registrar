using System.Text.RegularExpressions;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-ENG-07: unresolved bracketed placeholder tokens left in body prose. Body prose
// is rendered verbatim (only clauseInsertion substitutes placeholders), so a residual
// [TOKEN] ships literally. Scans non-clause blocks only; clauseInsertion is governed by
// ClauseResolver / QA-SF-11 and may legitimately retain placeholders. The token pattern
// is config data (config/style-rules.json contentLint.tokenResidue.pattern), not code.
public sealed class TokenResidueLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-07";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 7";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var pattern = context.Config.ContentLint.TokenResidue?.Pattern;
        if (string.IsNullOrEmpty(pattern)) return new[] { MissingPatternConfig() };

        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        try
        {
            for (var s = 0; s < sections.Count; s++)
            {
                var blocks = sections[s].Blocks;
                for (var b = 0; b < blocks.Count; b++)
                {
                    foreach (var (suffix, text) in ScanFields(blocks[b]))
                    {
                        if (text is null) continue;
                        var match = regex.Match(text);
                        if (match.Success)
                            checks.Add(Fail(sections[s].Title, match.Value,
                                $"/dataInputs/sections/{s}/blocks/{b}{suffix}"));
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A timeout means the configured pattern itself is pathological (e.g.
            // catastrophic backtracking); this is a config-level failure, not a
            // per-block finding, so scanning stops and a single check is returned.
            return new[] { PatternTimeout() };
        }
        return checks;
    }

    private static IEnumerable<(string Suffix, string? Text)> ScanFields(AgrBlock block)
    {
        switch (block.BlockType)
        {
            case "paragraph":
            case "warnCallout":
            case "copyReadyMessage":
            case "h2":
            case "h3":
                yield return ("/text", block.Text);
                break;
            case "bulletList":
                if (block.Items is not null)
                    yield return ("/items", string.Join(" ", block.Items));
                break;
            case "table":
                if (block.ColumnHeaders is not null)
                    yield return ("/columnHeaders", string.Join(" ", block.ColumnHeaders));
                if (block.Rows is not null)
                    yield return ("/rows", string.Join(" ", block.Rows.SelectMany(r => r)));
                break;
            // clauseInsertion and aggregationTable: not scanned here. Both are config-sourced
            // (clause text / the projected clause library), carry no AGR body text, and
            // legitimately contain [TOKEN] slots. Adding a case for either would fail closed
            // the very documents whose purpose is to display those slots. copyReadyMessage IS
            // scanned (above): it carries operator-facing authored text that is copied verbatim
            // to the client, so an unresolved token there must fail closed like any other
            // authored-prose block.
            // Any other unsupported type: also not scanned here (and separately fails closed
            // via QA-ENG-06 / BlockTypeSupportLayer before rendering is attempted).
        }
    }

    private static Check Fail(string title, string token, string jsonPath) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = $"Section '{title}' body prose contains an unresolved placeholder token '{token}'.",
        JsonPath = jsonPath,
        Expected = "no unresolved bracketed placeholder tokens in body prose",
        Actual = token,
        Remediation = $"Resolve '{token}' to its literal value in the body text of section '{title}'. "
            + "Body prose is not placeholder-substituted; only clause text resolves tokens at render time.",
        RuleSource = RuleSourceValue
    };

    private static Check MissingPatternConfig() => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = "The token-residue pattern is missing from config (contentLint.tokenResidue.pattern).",
        Expected = "contentLint.tokenResidue.pattern present in config/style-rules.json",
        Actual = "absent",
        Remediation = "Add contentLint.tokenResidue.pattern to config/style-rules.json.",
        RuleSource = RuleSourceValue
    };

    private static Check PatternTimeout() => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = "The token-residue pattern (contentLint.tokenResidue.pattern) timed out while matching body prose.",
        Expected = "contentLint.tokenResidue.pattern matches within the configured timeout",
        Actual = "regex match timeout",
        Remediation = "Replace contentLint.tokenResidue.pattern in config/style-rules.json with a linear-time pattern; the current pattern is pathological (e.g. catastrophic backtracking).",
        RuleSource = RuleSourceValue
    };
}
