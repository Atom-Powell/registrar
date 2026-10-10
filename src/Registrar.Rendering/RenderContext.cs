using Registrar.Core.Config;

namespace Registrar.Rendering;

public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio,
    WarnCalloutStyle Warn,
    IReadOnlyList<ClauseEntry> ClauseLibrary,
    CopyReadyMessageStyle CopyReadyMessage,
    IReadOnlyList<CanonicalToken> PlaceholderDictionary);
