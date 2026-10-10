using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests.Core;

public class AgrSerializerTests
{
    private static IEnumerable<string> FixturePaths() =>
        Directory.GetFiles(Path.Combine(SemanticTestSupport.RepoRoot(), "fixtures"), "agr-*.json")
            .OrderBy(p => p, StringComparer.Ordinal);

    // The strongest test available for free: every fixture, parsed and written back,
    // must still satisfy the schema. If the serializer emits anything the schema
    // rejects, the GUI can never build an artifact.
    //
    // The invariant asserted here is "serialization preserves schema validity", not
    // "serialization produces schema-valid output". A handful of fixtures are
    // deliberately built to be schema-invalid (they exercise QA-SCHEMA-01 and layers
    // that are only reachable by bypassing it) via a pattern or enum violation that the
    // C# model does not - and should not - enforce itself; that enforcement belongs to
    // the schema. Those fixtures cannot become valid by round tripping, and it is not
    // the serializer's job to fix them, so they are excluded by checking the ORIGINAL
    // text against the schema first, not by checking whether AgrParser can deserialize
    // it (every fixture deserializes; that alone doesn't mean it is schema-valid).
    [Fact]
    public void Round_tripped_fixtures_still_satisfy_the_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;
        var layer = new SchemaValidationLayer();

        var checkedCount = 0;
        var skipped = new List<string>();
        foreach (var path in FixturePaths())
        {
            var original = File.ReadAllText(path);

            var originalChecks = layer.Validate(new SchemaValidationInput(original, schema, schemaRoot));
            if (originalChecks.Any(c => c.Result == CheckResultValue.Fail))
            {
                skipped.Add(Path.GetFileName(path));
                continue;
            }

            var parsed = AgrParser.Parse(original);
            var rewritten = AgrSerializer.ToJson(parsed);
            var checks = layer.Validate(new SchemaValidationInput(rewritten, schema, schemaRoot));

            Assert.False(
                checks.Any(c => c.Result == CheckResultValue.Fail),
                $"{Path.GetFileName(path)} does not satisfy the schema after round trip: "
                    + string.Join("; ", checks.Where(c => c.Result == CheckResultValue.Fail).Select(c => c.Message)));
            checkedCount++;
        }

        Assert.True(
            checkedCount > 20,
            $"only {checkedCount} fixtures round tripped; expected the bulk of them. "
                + $"Skipped {skipped.Count} as already schema-invalid before round tripping: "
                + string.Join(", ", skipped));
    }

    // Schema validity is necessary but not sufficient: an absent optional property is
    // still schema-valid, so a serializer that silently drops one passes the schema
    // check above while quietly losing data. "Lossless" means the round-tripped
    // document is semantically identical to the original, not merely still valid, so
    // this compares the two as trees (ignoring key order and formatting) rather than
    // through the schema. Same skip rule as above: a fixture that was already
    // schema-invalid is excluded, since it is not this serializer's job to fix it.
    [Fact]
    public void Round_tripped_fixtures_are_semantically_identical()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;
        var layer = new SchemaValidationLayer();

        var checkedCount = 0;
        var skipped = new List<string>();
        var mismatches = new List<string>();
        foreach (var path in FixturePaths())
        {
            var original = File.ReadAllText(path);

            var originalChecks = layer.Validate(new SchemaValidationInput(original, schema, schemaRoot));
            if (originalChecks.Any(c => c.Result == CheckResultValue.Fail))
            {
                skipped.Add(Path.GetFileName(path));
                continue;
            }

            var parsed = AgrParser.Parse(original);
            var rewritten = AgrSerializer.ToJson(parsed);

            var originalTree = JsonNode.Parse(original);
            var rewrittenTree = JsonNode.Parse(rewritten);

            if (!JsonNode.DeepEquals(originalTree, rewrittenTree))
            {
                FirstDifference(originalTree, rewrittenTree, "$", out var diff);
                mismatches.Add($"{Path.GetFileName(path)}: {diff}");
            }
            checkedCount++;
        }

        Assert.True(
            checkedCount > 20,
            $"only {checkedCount} fixtures checked; expected the bulk of them. "
                + $"Skipped {skipped.Count} as already schema-invalid before round tripping: "
                + string.Join(", ", skipped));

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} fixture(s) lost or changed data on round trip:\n"
                + string.Join("\n", mismatches));
    }

    // Reports the first (in key/index order) point where two JSON trees diverge,
    // rather than a bare "not equal" that gives no way to diagnose a large document.
    private static void FirstDifference(JsonNode? original, JsonNode? rewritten, string path, out string diff)
    {
        if (JsonNode.DeepEquals(original, rewritten))
        {
            diff = $"{path}: no difference found (unexpected)";
            return;
        }

        if (original is JsonObject originalObject && rewritten is JsonObject rewrittenObject)
        {
            var keys = originalObject.Select(kv => kv.Key)
                .Union(rewrittenObject.Select(kv => kv.Key))
                .OrderBy(k => k, StringComparer.Ordinal);

            foreach (var key in keys)
            {
                var inOriginal = originalObject.TryGetPropertyValue(key, out var originalValue);
                var inRewritten = rewrittenObject.TryGetPropertyValue(key, out var rewrittenValue);

                if (inOriginal != inRewritten)
                {
                    var present = inOriginal ? originalValue : rewrittenValue;
                    diff = $"{path}/{key}: present only in {(inOriginal ? "original" : "round-tripped")} "
                        + $"(value: {present?.ToJsonString() ?? "null"})";
                    return;
                }

                if (!JsonNode.DeepEquals(originalValue, rewrittenValue))
                {
                    FirstDifference(originalValue, rewrittenValue, $"{path}/{key}", out diff);
                    return;
                }
            }

            diff = $"{path}: objects differ but no differing property was found (unexpected)";
            return;
        }

        if (original is JsonArray originalArray && rewritten is JsonArray rewrittenArray)
        {
            if (originalArray.Count != rewrittenArray.Count)
            {
                diff = $"{path}: array length differs (original has {originalArray.Count}, "
                    + $"round-tripped has {rewrittenArray.Count})";
                return;
            }

            for (var i = 0; i < originalArray.Count; i++)
            {
                if (!JsonNode.DeepEquals(originalArray[i], rewrittenArray[i]))
                {
                    FirstDifference(originalArray[i], rewrittenArray[i], $"{path}[{i}]", out diff);
                    return;
                }
            }

            diff = $"{path}: arrays differ but no differing element was found (unexpected)";
            return;
        }

        diff = $"{path}: original={original?.ToJsonString() ?? "null"}, "
            + $"round-tripped={rewritten?.ToJsonString() ?? "null"}";
    }

    // placeholderMap values may legitimately be null, meaning "retained for client
    // completion". WhenWritingNull applies to properties, not dictionary entries, but
    // that is worth proving rather than assuming.
    [Fact]
    public void Null_placeholder_values_survive_serialization()
    {
        var agr = AgrParser.Parse(SemanticTestSupport.FixtureText("agr-sample-ds-draft.json"));
        var withNull = agr with
        {
            DataInputs = agr.DataInputs with
            {
                PlaceholderMap = new Dictionary<string, string?> { ["[DATE]"] = null }
            }
        };

        var json = AgrSerializer.ToJson(withNull);
        var reparsed = AgrParser.Parse(json);

        Assert.True(reparsed.DataInputs.PlaceholderMap.ContainsKey("[DATE]"));
        Assert.Null(reparsed.DataInputs.PlaceholderMap["[DATE]"]);
    }

    [Fact]
    public void Serialization_is_deterministic()
    {
        var agr = AgrParser.Parse(SemanticTestSupport.FixtureText("agr-sample-tcl-draft.json"));

        Assert.Equal(AgrSerializer.ToJson(agr), AgrSerializer.ToJson(agr));
    }

    // No current fixture has an MST or internal-only artifact with client/engagement
    // explicitly null, but CLAUDE.md documents null as the expected shape for exactly
    // that case, so this constructs it directly rather than waiting for a fixture to
    // exercise it. client/engagement must behave the same as reviewDate and the other
    // properties already proven above: an explicit null is written as JSON null, not
    // dropped, and round trips back to null rather than to an absent (and therefore
    // ambiguous) property.
    [Fact]
    public void Null_client_and_engagement_survive_serialization()
    {
        var agr = AgrParser.Parse(SemanticTestSupport.FixtureText("agr-sample-ds-draft.json"));
        var withNulls = agr with
        {
            Context = agr.Context with { Client = null, Engagement = null }
        };

        var json = AgrSerializer.ToJson(withNulls);
        var tree = JsonNode.Parse(json)!.AsObject()["context"]!.AsObject();

        Assert.True(tree.ContainsKey("client"));
        Assert.Null(tree["client"]);
        Assert.True(tree.ContainsKey("engagement"));
        Assert.Null(tree["engagement"]);

        var reparsed = AgrParser.Parse(json);
        Assert.Null(reparsed.Context.Client);
        Assert.Null(reparsed.Context.Engagement);
    }
}
