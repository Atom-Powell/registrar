using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Import;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Import;

public class ImportRoundTripTests
{
    private static string AsChatReply(string json)
        => "Here is the AGR:\n\n```json\n" + json + "\n```\n\nAnything else?";

    // AgrDraft is a record whose collection members compare by reference under the
    // compiler-generated Equals, so two drafts with identical contents are never equal.
    // Canonical JSON compares contents instead, and does it without a per-member
    // assertion list: a dropped or mis-sourced property shows up as a difference in the
    // text, which is the whole point of this guard. Nulls are written rather than
    // omitted, so a field that vanished reads as a visible difference rather than an
    // absent line.
    private static string DraftJson(AgrDraft? draft)
        => JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });

    // The guard against a silently dropped field. ImportParser.ToDraft is a hand-written
    // mapping, the same shape of code that lost four fields during GUI-3, so it is held to
    // the same standard: every optional member populated, and the whole draft compared.
    [Fact]
    public void A_fully_populated_document_survives_serialize_then_import()
    {
        var populated = SemanticTestSupport.FullyPopulatedAgr();
        var expected = AgrAuthoring.FromDocument(populated);

        var result = ImportParser.Parse(
            AsChatReply(AgrSerializer.ToJson(populated)), SemanticTestSupport.Config());

        Assert.Empty(result.Findings);
        Assert.Equal(DraftJson(expected), DraftJson(result.Draft));
    }

    // Broad shape coverage for nearly no effort: every committed fixture is a different
    // arrangement of the schema, and all of them must survive the wrapper and come back
    // identical to what FromDocument produces from the same document.
    [Fact]
    public void Every_committed_fixture_survives_serialize_then_import()
    {
        var config = SemanticTestSupport.Config();
        var checkedCount = 0;

        foreach (var path in Directory.GetFiles(SemanticTestSupport.FixtureDirectory(), "*.json"))
        {
            AgrDocument document;
            try
            {
                document = AgrParser.Parse(File.ReadAllText(path));
            }
            catch (System.Text.Json.JsonException)
            {
                // Fixtures that deliberately violate the schema cannot be parsed into a
                // document, so there is nothing to round trip. They are covered by the
                // malformed-paste tests instead.
                continue;
            }

            var expected = AgrAuthoring.FromDocument(document);
            var result = ImportParser.Parse(AsChatReply(AgrSerializer.ToJson(document)), config);

            Assert.Equal(DraftJson(expected), DraftJson(result.Draft));
            checkedCount++;
        }

        Assert.True(checkedCount > 20, $"only {checkedCount} fixtures round tripped; expected most of them");
    }
}
