namespace Registrar.Core.Governor;

// Knowledge for the correction agent, which turns a failed validation report into a
// corrected request. It is the check catalogue and nothing else: the protocol for how to
// respond to a failure is hand-written prose and lives in governor-payload-correction.md,
// because it is judgment rather than data.
public sealed class CorrectionProjection : IKnowledgeProjection
{
    public string FileName => "correction.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Validation Check Catalogue",
            "The correction agent, which repairs a request the engine rejected.",
            "Say what every check means, so a failed report can be acted on rather than guessed at.",
            source.SourceHashes);

        doc.Para("Every entry in a validation report carries a `checkId`. Find it here. The "
            + "report's own `remediation` field is phrased as an instruction you can execute; "
            + "this catalogue tells you what rule produced it and where that rule comes from.");

        doc.Para("A failed check is not a bug to argue with. Fix the exact field the report "
            + "names, change nothing else, and resubmit.");

        doc.Heading("Checks");
        doc.Table(
            new[] { "Check", "Stage", "Fails as", "Rule" },
            config.Qa.Entries.Select(e => (IReadOnlyList<string>)new[]
            {
                $"`{e.CheckId}`",
                string.IsNullOrWhiteSpace(e.Layer) ? "not set" : e.Layer,
                string.IsNullOrWhiteSpace(e.ErrorCategory) ? "not set" : e.ErrorCategory,
                (e.Rule ?? "").Replace("|", "\\|")
            }));

        doc.Heading("Rule sources");
        doc.Para("Where each rule comes from, when you need to check the governing document.");
        doc.Table(
            new[] { "Check", "Source" },
            config.Qa.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.RuleSource))
                .Select(e => (IReadOnlyList<string>)new[] { $"`{e.CheckId}`", e.RuleSource! }));

        return doc.Build();
    }
}
