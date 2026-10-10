using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// The per-type drafting brief. It lives here rather than inline in DrafterProjection so
// the GUI can hand a technician the brief for the one type they are drafting, using the
// same code that generates the drafting agent's knowledge. One code path means the
// clipboard brief and docs/governor/drafter.md cannot drift apart.
public static class GovernorBrief
{
    private const string DefinedStatus = "defined";

    // Writes one type's block into an existing document. DrafterProjection calls this in
    // its loop, having already filtered to in-scope types, so this method does not
    // re-check scope.
    public static void Write(KnowledgeDocument doc, ArtifactTypeConfig type, ConfigBundle config)
    {
        doc.Sub($"{type.Code} - {type.Name}");

        doc.Para($"Classification: {type.Classification ?? "not set"}. Layer: {type.Layer ?? "not set"}.");

        doc.Para("Sections, in this exact order:");
        var n = 1;
        foreach (var title in type.SectionSchema!.OrderedSections!)
            doc.Bullet($"{n++}. {title}");
        doc.EndBullets();

        if (type.RequiredClauses is { Count: > 0 })
        {
            doc.Para("Required clauses. Emit each as a `clauseInsertion` block carrying only "
                + "the clause ID, in the section where it belongs. Never write clause prose: "
                + "the engine inserts the canonical text and proves it with SHA-256.");
            foreach (var id in type.RequiredClauses)
                doc.Bullet(config.ClausesById.TryGetValue(id, out var clause)
                    ? $"`{id}` - {clause.Name}"
                    : $"`{id}`");
            doc.EndBullets();
        }

        if (type.RequiredPlaceholders is { Count: > 0 })
        {
            doc.Para("Required placeholder tokens:");
            foreach (var token in type.RequiredPlaceholders)
                doc.Bullet($"`{token}`");
            doc.EndBullets();
        }

        if (type.UsesRadioOptionPattern)
            doc.Para("This type uses the radio-option pattern. Selection items must be "
                + "`radioQuestion` blocks carrying at least four options. The renderer applies "
                + "the glyph, the indentation, and the descriptor line; supply labels only.");
    }

    // The same block on its own, for the clipboard. Rejects a type that has no brief
    // rather than returning an empty or misleading one: asking for a brief for SCL is a
    // caller error, not a runtime condition, and the GUI offers only in-scope types.
    public static string For(ArtifactTypeConfig type, ConfigBundle config)
    {
        if (!type.GovernorScope)
            throw new ArgumentException(
                $"{type.Code} is outside Governor scope, so it has no drafting brief.",
                nameof(type));

        if (type.SectionSchema?.Status != DefinedStatus || type.SectionSchema.OrderedSections is null)
            throw new ArgumentException(
                $"{type.Code} has no defined section schema, so it has no drafting brief.",
                nameof(type));

        var doc = KnowledgeDocument.Fragment();
        Write(doc, type, config);
        return doc.Build();
    }
}
