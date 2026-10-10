using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the drafting agent: the structure and required content of every artifact
// the Governor may be asked to produce. A type is included only when both hold: it has a
// defined section schema (types without one are refused by the engine, QA-ENG-02, and
// listing them would invite the agent to attempt one) and it carries GovernorScope=true
// (SCL and PDX have defined section schemas but are engine-generated master-pack
// reference documents outside the Governor's scope, so they are excluded here even
// though the engine can render them).
public sealed class DrafterProjection : IKnowledgeProjection
{
    private const string DefinedStatus = "defined";

    public string FileName => "drafter.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Artifact Structure and Required Content",
            "The drafting agent, which produces artifact content.",
            "Give the exact structure and required content of every artifact the engine can "
                + "generate.",
            source.SourceHashes);

        doc.Para("Section titles below are exact. The engine compares them character for "
            + "character and in order, and rejects any addition, omission, reordering, or "
            + "rewording. Do not improve them.");

        var inScope = config.ArtifactTypesByCode.Values
            .Where(t => t.GovernorScope
                && t.SectionSchema?.Status == DefinedStatus
                && t.SectionSchema.OrderedSections is not null)
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        doc.Heading("Artifact types");

        foreach (var t in inScope)
            GovernorBrief.Write(doc, t, config);

        doc.Heading("Block types");
        doc.Para("These are the only block types the engine renders. Any other value is rejected "
            + "before rendering is attempted.");
        foreach (var bt in BlockTypes(source.AgrSchema))
            doc.Bullet($"`{bt}`");
        doc.EndBullets();

        doc.Para("The schema does not restrict which of the fields below belong to which block "
            + "type: they are declared as optional siblings of every block, not per type. Each "
            + "block type uses only the subset it needs; consult the sample AGRs for how each "
            + "block type is actually shaped.");
        doc.Table(
            new[] { "Field", "Type", "Description" },
            CompanionFields(source.AgrSchema).Select(f => (IReadOnlyList<string>)new[]
            {
                $"`{f.Field}`",
                f.Type,
                f.Description
            }));

        doc.Heading("Character policy");
        doc.Para("Free text and placeholder values may contain only the characters below. A "
            + "prohibited character fails the run in both draft and promotion. Use a plain "
            + "hyphen, straight quotes, and three periods.");
        doc.Bullet("Printable Basic Latin.");
        doc.Bullet("Printable Latin-1 Supplement, excluding the non-breaking space and the soft "
            + "hyphen, so accented names are fine.");
        if (config.ContentLint.ApprovedGlyphAllowlist is { Count: > 0 })
            doc.Bullet("Approved glyphs: "
                + string.Join(", ", config.ContentLint.ApprovedGlyphAllowlist.Select(g => $"`{g}`")));
        doc.EndBullets();

        return doc.Build();
    }

    private static IReadOnlyList<string> BlockTypes(JsonNode schema)
    {
        var node = schema["$defs"]!["contentBlock"]!["properties"]!["blockType"]!["enum"]!;
        return node.AsArray().Select(n => (string)n!).ToList();
    }

    // Companion fields shared by every block type, read from $defs.contentBlock.properties.
    // The schema declares them as optional siblings with no per-blockType association, so
    // this list is emitted once rather than mapped to individual block types: a mapping
    // the schema does not contain must not be invented here.
    private static IReadOnlyList<(string Field, string Type, string Description)> CompanionFields(JsonNode schema)
    {
        var props = schema["$defs"]!["contentBlock"]!["properties"]!.AsObject();
        var result = new List<(string, string, string)>();
        foreach (var (name, node) in props)
        {
            if (name == "blockType") continue;
            var typeNode = node!["type"];
            var type = typeNode is JsonArray typeArray
                ? string.Join(" or ", typeArray.Select(n => (string)n!))
                : (string)typeNode!;
            var description = (string?)node["description"];
            result.Add((name, type, string.IsNullOrWhiteSpace(description) ? "not set" : description));
        }
        return result;
    }
}
