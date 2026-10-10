using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Registrar.Core.Config;
using Registrar.Core.Engagement;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// A new artifact from the five things a technician actually knows: type, Document ID,
// client, engagement, owner. Everything else is derived, and derived values stay out of
// the draft so config remains the source of truth for them.
public static class DraftCreation
{
    // Rule 6. An artifact type whose section schema is not defined refuses generation with
    // a ConfigurationError, so offering it for creation would produce a draft that can
    // never build. Ordered by code so the picker is stable between runs.
    public static IReadOnlyList<ArtifactTypeConfig> CreatableTypes(ConfigBundle config)
        => config.ArtifactTypesByCode.Values
            .Where(t => t.SectionSchema?.Status == "defined" && t.SectionSchema.OrderedSections is not null)
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

    // Why a Document ID is not usable, or null when it is. Rule 5 stands: this validates
    // an ID somebody else issued, and never generates or corrects one. A value that does
    // not pass is refused whole, not trimmed into shape, because a corrected ID is an ID
    // this engine minted.
    //
    // Two independent reasons, checked in that order. The first is that the ID becomes a
    // file name inside the engagement folder, and Path.Combine treats a second argument
    // that is rooted or that walks upward as the whole path: "..\..\OtherClient\X" writes
    // into another client's folder and an absolute path writes wherever it points. This is
    // a desktop app inside an MSP network handling client engagement data, so that is a
    // containment failure, not a cosmetic one. The second is the schema's own documentId
    // pattern, which is the engine's existing answer to what an issued ID looks like and
    // is read from the schema rather than restated here.
    //
    // The path check is not redundant with the pattern check even though the pattern
    // admits no separator today. The pattern is data that a schema release can widen; the
    // containment property must hold whatever it says.
    public static string? DocumentIdRefusal(string documentId, JsonNode agrSchema)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return "A Document ID is required. Registry Owner issues it; this screen never mints one.";

        if (!string.Equals(Path.GetFileName(documentId), documentId, StringComparison.Ordinal))
            return $"'{documentId}' is not usable as a Document ID: it names a path rather than an "
                + "identifier, and a draft is only ever written inside the engagement folder.";

        var pattern = (string?)agrSchema["properties"]!["artifactIdentity"]!["properties"]!
            ["documentId"]!["pattern"];

        if (pattern is null)
            return null;

        // Rule 3 makes config editable without a rebuild, so this pattern is a value a human
        // can hand-edit between releases. Config is trusted input and this is not an
        // injection surface, but a catastrophically backtracking pattern would spin here
        // with the UI thread blocked and nothing on screen to say why. The timeout bounds
        // that, and the refusal names the config file rather than the operator's input: the
        // ID is not what failed, and it is not something they can fix from this screen.
        try
        {
            if (!Regex.IsMatch(documentId, pattern, RegexOptions.None, PatternMatchTimeout))
                return $"'{documentId}' is not the shape of an issued Document ID. Registry Owner "
                    + "issues IDs; ask for one rather than composing it here.";
        }
        catch (RegexMatchTimeoutException)
        {
            return "The Document ID pattern in agr.schema.json could not be evaluated within "
                + $"{PatternMatchTimeout.TotalMilliseconds:0} ms. This is a configuration problem, not a "
                + $"problem with '{documentId}'. Report the pattern rather than changing the ID.";
        }

        return null;
    }

    // Generous by three orders of magnitude for the anchored, separator-free patterns an
    // issued Document ID uses, and short enough that a hung pattern cannot read as a frozen
    // window. Only a pattern that backtracks catastrophically can reach it.
    private static readonly TimeSpan PatternMatchTimeout = TimeSpan.FromMilliseconds(250);

    // Callers must have cleared the id through DocumentIdRefusal first. The separator check
    // is repeated here because this is the function that produces the path segment, and a
    // future caller that forgets the gate must fail loudly rather than write outside the
    // engagement folder.
    public static string FileName(string documentId)
    {
        if (!string.Equals(Path.GetFileName(documentId), documentId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"'{documentId}' names a path rather than a Document ID, so no draft file name "
                + "can be built from it.",
                nameof(documentId));

        return documentId + DraftFile.Extension;
    }

    public static AgrDraft New(
        ArtifactTypeConfig type,
        string documentId,
        string? client,
        string? engagement,
        string owner,
        ConfigBundle config,
        JsonNode agrSchema,
        IClock clock,
        string executionId)
    {
        // ReviewDate is derived exactly once, here. AgrDraft is explicit that a converted
        // document must never have a review date invented for it on save, so this is the
        // only place the category interval is turned into a date.
        var defaults = AgrAuthoring.Defaults(type, config, agrSchema, clock);

        var draft = new AgrDraft
        {
            ArtifactType = type.Code,
            DocumentId = documentId,
            Client = string.IsNullOrWhiteSpace(client) ? null : client,
            Engagement = string.IsNullOrWhiteSpace(engagement) ? null : engagement,
            Owner = owner,
            ReviewDate = defaults.ReviewDate,
            Mode = Mode.Draft,
            SourceWorkflow = new AgrSourceWorkflowContext
            {
                WorkflowName = "operator-gui",
                Phase = "authoring",
                ExecutionId = executionId
            }
        };

        // Materialized up front so the editor opens with a panel per section and the
        // indices agree with the pointers from the first render.
        return DraftEdits.NormalizeSections(draft, type);
    }
}
