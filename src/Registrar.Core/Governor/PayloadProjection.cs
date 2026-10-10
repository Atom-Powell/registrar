using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the payload-assembling agent: the envelope, the identity rules, and the
// closed vocabularies. Everything here is a hard gate; none of it is a matter of taste.
public sealed class PayloadProjection : IKnowledgeProjection
{
    public string FileName => "payload.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;
        var v = config.Vocabulary;

        var doc = new KnowledgeDocument(
            "AGR Envelope and Identity Rules",
            "The payload-assembling agent, which turns content into a valid request.",
            "Give the exact shape of an Artifact Generation Request and the rules its identity "
                + "fields must satisfy.",
            source.SourceHashes);

        doc.Heading("Required top-level fields");
        foreach (var field in RequiredFields(source.AgrSchema))
            doc.Bullet($"`{field}`");
        doc.EndBullets();

        doc.Heading("Identity");
        doc.Bullet($"`documentId` must match `{v.DocumentIdPattern}`. The pattern encodes which "
            + "category and code may pair, so a mismatched pairing fails at the schema, not "
            + "later. The embedded code must also equal `artifactType`.");
        doc.Bullet("**Never mint a Document ID.** Registry Owner issues them and the ID must "
            + "already exist in the registry. If none exists, ask for one.");
        doc.Bullet($"`version` must match `{v.VersionPattern}`.");
        doc.Bullet($"`classification` is one of: {Join(v.Classifications)}.");
        doc.Bullet($"`layer` is one of: {Join(v.FrameworkLayers)}. It is determined by the "
            + "artifact type, not chosen. A disagreement with config fails the run.");
        doc.Bullet($"`approvalStatus` is one of: {Join(config.ApprovalStatuses.OrderBy(s => s, StringComparer.Ordinal))}. "
            + $"A new artifact is always `{v.InitialState}`.");
        doc.EndBullets();

        doc.Heading("Prohibited status aliases");
        doc.Para($"Never use: {Join(v.ProhibitedStatusAliases)}. They are not controlled values.");

        doc.Heading("Legal lifecycle transitions");
        doc.Table(
            new[] { "From", "May become" },
            config.Transitions
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => (IReadOnlyList<string>)new[]
                {
                    t.Key,
                    t.Value.Count == 0 ? "nothing, this state is terminal" : Join(t.Value)
                }));

        doc.Heading("Review dates");
        doc.Para("A review date must fall within the category interval measured from today, "
            + "unless a documentation-owner override is attached. Do not fabricate an override "
            + "to dodge the interval.");
        doc.Table(
            new[] { "Category", "Interval" },
            config.ReviewIntervalMonthsByCategory
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => (IReadOnlyList<string>)new[]
                {
                    k.Key,
                    k.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " months"
                }));

        doc.Heading("Rendering options");
        var pattern = (string?)source.AgrSchema["properties"]!["renderingOptions"]!
            ["properties"]!["outputFileName"]!["pattern"];
        if (pattern is not null)
            doc.Bullet($"`outputFileName` must match `{pattern}`.");
        doc.Bullet("`packageName` is the footer package value and must not be empty.");
        doc.EndBullets();

        return doc.Build();
    }

    private static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.Select(v => $"`{v}`"));

    private static IReadOnlyList<string> RequiredFields(JsonNode schema) =>
        schema["required"]!.AsArray().Select(n => (string)n!).ToList();
}
