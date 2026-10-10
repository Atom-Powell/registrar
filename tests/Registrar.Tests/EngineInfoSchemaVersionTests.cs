using System.Text.Json;
using Registrar.Core;

namespace Registrar.Tests;

// Guard: EngineInfo.SchemaVersion has no runtime call site, so nothing otherwise
// catches a developer bumping the schemaVersion const in agr.schema.json without
// bumping the engine constant (or vice versa). This test pins the two together so
// they can only move as a pair.
public class EngineInfoSchemaVersionTests
{
    [Fact]
    public void EngineInfo_SchemaVersion_matches_the_agr_schema_const()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var schemaConst = doc.RootElement
            .GetProperty("properties")
            .GetProperty("schemaVersion")
            .GetProperty("const")
            .GetString();

        Assert.Equal(EngineInfo.SchemaVersion, schemaConst);
    }
}
