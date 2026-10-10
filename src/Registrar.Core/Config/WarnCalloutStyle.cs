namespace Registrar.Core.Config;

// The warn-callout presentation (config-driven so a v1.x template pack is a config
// release, not a rebuild). Only the label lives here; the box colors are the existing
// visualIdentity keys warnRedBorder / warnRedFill, referenced by the renderer.
public sealed record WarnCalloutStyle
{
    public required string Label { get; init; }
}
