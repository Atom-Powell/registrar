namespace Registrar.Core.Config;

// The screen palette, deliberately separate from StyleRules. The renderer binds
// visualIdentity and must never see this block: these colors were chosen against WCAG
// thresholds for a monitor, and some of them would be wrong in print.
public sealed record ScreenTokens
{
    public required IReadOnlyDictionary<string, string> Colors { get; init; }
}
