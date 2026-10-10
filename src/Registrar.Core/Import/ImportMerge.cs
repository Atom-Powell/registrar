using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Import;

public sealed record ImportMergeResult(AgrDraft Draft, IReadOnlyList<ImportFinding> Findings);

// What a pasted Governor reply is allowed to change about an open draft: its content, and
// nothing else.
//
// ImportParser returns a whole AgrDraft, so a reply can carry an artifact type, Document ID,
// classification or owner. Applying those would let a chat reply move an identifier that
// Registry Owner issued and an operator chose, which is precisely what CLAUDE.md rule 5
// and the division of labor forbid: the engine never trusts the Governor.
//
// A disagreement is reported rather than dropped, because a reply carrying the wrong Document
// ID usually means the operator pasted the wrong reply, and that is worth seeing. An omitted
// field is not a disagreement: most replies will not echo identity at all, and reporting
// those would train the operator to ignore the list.
public static class ImportMerge
{
    public static ImportMergeResult Apply(AgrDraft open, AgrDraft imported, ArtifactTypeConfig type)
    {
        var findings = new List<ImportFinding>();

        void Compare(string? theirs, string? ours, string jsonPath, string label)
        {
            if (string.IsNullOrWhiteSpace(theirs)) return;
            if (string.Equals(theirs, ours, StringComparison.Ordinal)) return;

            findings.Add(new ImportFinding(
                jsonPath,
                $"The reply carried {label} '{theirs}', which does not match '{ours}' on this draft. "
                + "It was not applied.",
                ImportFindingKind.IdentityConflict));
        }

        Compare(imported.ArtifactType, open.ArtifactType, FieldPath.ArtifactType, "artifact type");
        Compare(imported.DocumentId, open.DocumentId, FieldPath.DocumentId, "Document ID");
        Compare(imported.Owner, open.Owner, FieldPath.Owner, "owner");
        Compare(imported.Client, open.Client, FieldPath.Client, "client");

        // Content only. Sections replace rather than merge: the Governor drafts a whole
        // artifact, and interleaving its sections with whatever was already there would
        // produce a document neither party wrote.
        var merged = open with
        {
            Sections = imported.Sections.Count > 0 ? imported.Sections : open.Sections,
            PlaceholderMap = imported.PlaceholderMap.Count > 0 ? imported.PlaceholderMap : open.PlaceholderMap
        };

        return new ImportMergeResult(DraftEdits.NormalizeSections(merged, type), findings);
    }
}
