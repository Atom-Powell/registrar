using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class PlaceholderLayer : ISemanticLayer
{
    public const string CheckId = "QA-SPEC-07";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 7";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var canonical = context.Config.CanonicalTokens.Select(t => t.Token).ToHashSet();
        var aliasToCanonical = new Dictionary<string, string>();
        foreach (var t in context.Config.CanonicalTokens)
            foreach (var alias in t.KnownDriftAliases)
                aliasToCanonical[alias] = t.Token;

        foreach (var (key, value) in context.Agr.DataInputs.PlaceholderMap)
        {
            if (aliasToCanonical.TryGetValue(key, out var canonicalForm))
            {
                checks.Add(Fail(
                    $"Placeholder key '{key}' is a known drift alias.",
                    PointerFor(key), canonicalForm, key,
                    $"Replace the placeholder key '{key}' with the canonical token '{canonicalForm}' in dataInputs.placeholderMap and all section blocks."));
                continue;
            }

            if (!canonical.Contains(key))
            {
                checks.Add(ModeGated(context.Mode,
                    $"Placeholder key '{key}' is not a known canonical token.",
                    PointerFor(key), "a canonical token from placeholder-dictionary.json", key,
                    $"Confirm '{key}' against the canonical Placeholder Dictionary; if it is canonical, add it to config/placeholder-dictionary.json."));
                continue;
            }

            if (value is null)
                checks.Add(ModeGated(context.Mode,
                    $"Placeholder '{key}' is unresolved (null).",
                    PointerFor(key), "a resolved value at promotion", "null",
                    $"Provide a value for '{key}' before promotion, or keep it null only while the artifact remains in Draft."));
        }

        context.Config.ArtifactTypesByCode.TryGetValue(context.Agr.Identity.ArtifactType, out var type);
        var requiredTokens = type?.RequiredPlaceholders;

        if (requiredTokens is not null)
        {
            foreach (var token in requiredTokens)
                if (!context.Agr.DataInputs.PlaceholderMap.ContainsKey(token))
                    checks.Add(Fail(
                        $"Required placeholder '{token}' is missing from placeholderMap.",
                        "/dataInputs/placeholderMap", $"a placeholderMap entry for '{token}'", "absent",
                        $"Add '{token}' to dataInputs.placeholderMap with a resolved value."));
        }

        // Coverage rollup (REG-TD-007): an informational summary of placeholder completeness,
        // always emitted in both modes. Inert to overall result (pass/info). Reports "beyond the
        // required set" rather than "unused" because this layer runs pre-render and cannot prove a
        // provided token is unreferenced (a non-required token may feed clause text at render).
        var requiredSet = requiredTokens is not null ? requiredTokens.ToHashSet() : new HashSet<string>();
        var providedKeys = context.Agr.DataInputs.PlaceholderMap.Keys;

        var missing = requiredSet.Where(t => !context.Agr.DataInputs.PlaceholderMap.ContainsKey(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        var beyondRequired = providedKeys.Where(k => canonical.Contains(k) && !requiredSet.Contains(k))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();

        checks.Add(Coverage(
            context.Agr.Identity.DocumentId,
            requiredSet.Count,
            context.Agr.DataInputs.PlaceholderMap.Count,
            missing,
            beyondRequired));

        return checks;
    }

    private static Check Coverage(
        string documentId, int requiredCount, int providedCount,
        IReadOnlyList<string> missing, IReadOnlyList<string> beyondRequired)
    {
        static string List(IReadOnlyList<string> tokens)
            => tokens.Count == 0 ? "none" : string.Join(", ", tokens);

        var message =
            $"Placeholder coverage for {documentId}: {requiredCount} required, {providedCount} provided, " +
            $"{missing.Count} missing, {beyondRequired.Count} beyond the required set. " +
            $"Missing: {List(missing)}. Beyond required: {List(beyondRequired)}.";

        return new Check
        {
            CheckId = CheckId,
            LayerName = Layer.Content,
            Result = CheckResultValue.Pass,
            SeverityLevel = Severity.Info,
            Message = message,
            JsonPath = "/dataInputs/placeholderMap",
            RuleSource = RuleSourceValue
        };
    }

    // Placeholder tokens contain '/', which must be escaped in a JSON Pointer.
    private static string PointerFor(string token)
        => "/dataInputs/placeholderMap/" + token.Replace("~", "~0").Replace("/", "~1");

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation)
        => Build(CheckResultValue.Fail, Severity.Blocking, message, jsonPath, expected, actual, remediation);

    private static Check ModeGated(string mode, string message, string jsonPath, string expected, string actual, string remediation)
    {
        var promotion = mode == Mode.Promotion;
        return Build(
            promotion ? CheckResultValue.Fail : CheckResultValue.Warn,
            promotion ? Severity.Blocking : Severity.Warning,
            message, jsonPath, expected, actual, remediation);
    }

    private static Check Build(string result, string severity, string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = result,
        SeverityLevel = severity,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
