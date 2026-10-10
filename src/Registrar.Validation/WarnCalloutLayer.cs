using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-SF-09: zero warn (red) callouts AND zero internal-only notes in client-facing
// output at promotion. Client-facing status is read from the artifact type's CONFIGURED
// classification (config/artifact-types.json), never the Governor-supplied
// artifactIdentity.classification. An internal-only note is any block with internalOnly=true
// (a flag on an otherwise-supported block, so it still renders in internally-classified
// artifacts); the warn-callout half also remains the gate once a future slice enables
// warnCallout rendering (today QA-ENG-06 additionally rejects the warnCallout block type).
public sealed class WarnCalloutLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-09";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 9";
    private const string ClientFacing = "Client-Facing";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Mode != Mode.Promotion) return Array.Empty<Check>();

        // Only the exact configured classification "Client-Facing" is gated here; other
        // classifications (e.g. "Controlled Signoff", "Internal Working Document") are
        // intentionally out of scope per the catalog's appliesTo: "classification=Client-Facing".
        // ClientFacing mirrors the value in config/artifact-types.json / config/vocabularies.json;
        // WarnCalloutLayerTests.QA_SF_09_fails_on_warn_callout_in_client_facing_promotion (which
        // depends on DS being Client-Facing in config) fails if that mapping is renamed.
        var artifactType = context.Agr.Identity.ArtifactType;
        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            || type.Classification != ClientFacing)
            return Array.Empty<Check>();

        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
            {
                // warnCallout takes precedence when a block is both a warn callout and
                // internalOnly, so the reported reason names the stronger structural signal.
                if (blocks[b].BlockType == "warnCallout")
                    checks.Add(Fail(sections[s].Title, s, b, "a warn callout", "warnCallout",
                        "no warnCallout blocks in client-facing output at promotion",
                        "Remove the warn callout"));
                else if (blocks[b].InternalOnly == true)
                    checks.Add(Fail(sections[s].Title, s, b, "an internal-only note", "internalOnly",
                        "no internal-only blocks in client-facing output at promotion",
                        "Remove the internal-only note"));
            }
        }
        return checks;
    }

    private static Check Fail(string title, int s, int b, string reason, string actual, string expected, string removalVerb) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = $"Section '{title}' contains {reason}, which is prohibited in client-facing output at promotion.",
        JsonPath = $"/dataInputs/sections/{s}/blocks/{b}",
        Expected = expected,
        Actual = actual,
        Remediation = $"{removalVerb} from section '{title}' before promotion, or move the note into an internal-only artifact.",
        RuleSource = RuleSourceValue
    };
}
