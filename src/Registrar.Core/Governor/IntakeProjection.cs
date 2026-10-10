using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the intake agent (the Steward), whose job is deciding which artifact an
// engagement needs. It deliberately carries no section schemas, clause IDs, or block
// types: an intake agent holding structural detail is an intake agent that starts
// drafting.
public sealed class IntakeProjection : IKnowledgeProjection
{
    public string FileName => "intake.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Artifact Catalogue",
            "The intake agent, which works out which artifact an engagement needs.",
            "Name every artifact type, say what it is for, and say whether the engine can "
                + "generate it yet.",
            source.SourceHashes);

        doc.Para("This catalogue is the complete set. If a request does not match one of these, "
            + "say so rather than inventing a type. If the type exists but is not generable, say "
            + "that too: the engine refuses to generate a type whose structure has not been "
            + "authored, and it will not improvise one.");

        var rows = config.ArtifactTypesByCode.Values
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Code,
                t.Name ?? "",
                t.Category,
                t.Classification ?? "not set",
                t.GovernorScope ? "yes" : "no",
                config.ReviewIntervalMonthsByCategory.TryGetValue(t.Category, out var m)
                    ? m.ToString(System.Globalization.CultureInfo.InvariantCulture) + " months"
                    : "",
                t.Description ?? ""
            });

        doc.Heading("Every artifact type");
        doc.Table(
            new[] { "Code", "Name", "Category", "Classification", "You can produce", "Review interval", "Description" },
            rows);

        doc.Para("Some types the engine can render are still outside your scope: they are "
            + "engine-generated master-pack reference documents produced directly by whoever "
            + "maintains the master pack, not client-engagement artifacts you are asked to "
            + "draft. If asked for one, say it exists but is not something you produce.");

        doc.Heading("Default audiences");
        doc.Para("Use these unless the engagement gives you a reason to narrow them.");
        foreach (var t in config.ArtifactTypesByCode.Values
                     .Where(t => !string.IsNullOrWhiteSpace(t.AudienceDefault))
                     .OrderBy(t => t.Code, StringComparer.Ordinal))
            doc.Bullet($"**{t.Code}**: {t.AudienceDefault}");
        doc.EndBullets();

        doc.Heading("What you do not decide");
        doc.Bullet("Document IDs. Registry Owner issues them; the engine never mints one. "
            + "If no ID exists, say so and stop.");
        doc.Bullet("Section structure, clause selection, or placeholder values. Those belong to "
            + "the drafting agent.");
        doc.EndBullets();

        return doc.Build();
    }
}
