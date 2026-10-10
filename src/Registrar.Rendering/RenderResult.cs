using Registrar.Core.Model;

namespace Registrar.Rendering;

public sealed record RenderResult(
    byte[] Bytes,
    IReadOnlyList<Check> PostRenderChecks,
    IReadOnlyList<ClauseInsertionAudit> ClauseAudits);
