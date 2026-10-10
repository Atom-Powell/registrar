namespace Registrar.Core.Config;

// The copy-ready-message presentation (config-driven so a template pack is a config
// release, not a rebuild). Only the label lives here; the box colors are existing
// visualIdentity keys (borderNeutral / dchFill / headingNavy), referenced by the renderer.
public sealed record CopyReadyMessageStyle
{
    public required string Label { get; init; }
}
