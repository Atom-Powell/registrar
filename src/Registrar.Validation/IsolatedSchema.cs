using Json.Schema;

namespace Registrar.Validation;

/// <summary>
/// JsonSchema.Net registers a compiled schema globally by its declared "$id".
/// Both agr.schema.json and validation-report.schema.json declare fixed $id
/// values, so building either against the default/global registry more than
/// once in the same process throws "Overwriting registered schemas is not
/// permitted" (see task-4-report.md and task-5-report.md). Every build in this
/// codebase therefore uses a fresh, isolated SchemaRegistry -- nothing is ever
/// registered globally, so repeated builds in the same process never collide.
/// </summary>
public static class IsolatedSchema
{
    public static JsonSchema FromFile(string path)
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        return JsonSchema.FromFile(path, options);
    }

    public static JsonSchema FromText(string text)
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        return JsonSchema.FromText(text, options);
    }
}
