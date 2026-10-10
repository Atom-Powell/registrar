using System.Text.Json.Nodes;
using Registrar.Core.Model;

namespace Registrar.Core.Config;

public sealed record QaCatalogEntry(
    string CheckId,
    string? RuleSource,
    string? ErrorCategory,
    string? Rule = null,
    string? Layer = null,
    string? AppliesTo = null);

/// <summary>
/// The QA checklist (config/qa-checklist.json) as the authoritative source of each
/// check's governance metadata. A check's ruleSource and errorCategory are stamped
/// from the catalog by checkId as the validation report is assembled, so the catalog -
/// not code constants - is the single home for those values. Dynamic per-instance
/// fields (message, jsonPath, expected, actual, remediation) stay in code because the
/// catalog carries no per-failure text.
/// </summary>
public sealed class QaCatalog
{
    private readonly IReadOnlyDictionary<string, QaCatalogEntry> _byId;

    public QaCatalog(IReadOnlyDictionary<string, QaCatalogEntry> byId) => _byId = byId;

    public bool Contains(string checkId) => _byId.ContainsKey(checkId);

    // Ordered so the generated correction knowledge is byte-stable across runs.
    public IReadOnlyList<QaCatalogEntry> Entries =>
        _byId.Values.OrderBy(e => e.CheckId, StringComparer.Ordinal).ToList();

    public QaCatalogEntry? Lookup(string checkId)
        => _byId.TryGetValue(checkId, out var entry) ? entry : null;

    public Check Stamp(Check check)
    {
        if (!_byId.TryGetValue(check.CheckId, out var entry))
            throw new InvalidOperationException(
                $"Check '{check.CheckId}' is not defined in qa-checklist.json; every emitted check must be cataloged.");

        return check with
        {
            // ruleSource is one-per-checkId, so the catalog is authoritative.
            RuleSource = entry.RuleSource ?? check.RuleSource,
            // errorCategory classifies a violation: passing checks carry none (code
            // signals that with null), and the catalog's category is authoritative
            // wherever it declares one. A check that emits more than one category
            // across branches (QA-ENG-07: MetadataViolation vs ConfigurationError) is
            // deliberately left uncatalogued so its per-branch code value stands.
            ErrorCategory = check.ErrorCategory is null ? null : (entry.ErrorCategory ?? check.ErrorCategory)
        };
    }

    public IReadOnlyList<Check> StampAll(IReadOnlyList<Check> checks)
    {
        var stamped = new List<Check>(checks.Count);
        foreach (var check in checks) stamped.Add(Stamp(check));
        return stamped;
    }

    public static QaCatalog Parse(string qaChecklistJson)
    {
        var root = JsonNode.Parse(qaChecklistJson)!;
        var byId = new Dictionary<string, QaCatalogEntry>(StringComparer.Ordinal);
        foreach (var node in root["checks"]!.AsArray())
        {
            var checkId = (string)node!["checkId"]!;
            byId[checkId] = new QaCatalogEntry(
                checkId,
                (string?)node["ruleSource"],
                (string?)node["errorCategory"],
                (string?)node["rule"],
                (string?)node["layer"],
                (string?)node["appliesTo"]);
        }
        return new QaCatalog(byId);
    }
}
