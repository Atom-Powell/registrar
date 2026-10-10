namespace Registrar.Rendering;

/// <summary>
/// Fixed relationship IDs for every part the renderer adds.
///
/// The Open XML SDK auto-generates a random ID (for example "Rbe9d392fa2464938") for any
/// part added without one. Those IDs are written into document.xml and the .rels parts, so
/// the same AGR produced different package bytes on every render and artifactSha256 could
/// not be reproduced. The hash is offered to the operator as proof of the artifact; a hash
/// that changes every run proves nothing. Naming every relationship removes that variance.
///
/// These values are part of the rendered output. Changing one changes every document's
/// bytes and hash, so treat them as fixed.
/// </summary>
internal static class RelationshipIds
{
    public const string MainDocument = "rId1";
    public const string CoreProperties = "rId2";
    public const string Numbering = "rId3";
    public const string Footer = "rId4";
    public const string Logo = "rId5";
}
