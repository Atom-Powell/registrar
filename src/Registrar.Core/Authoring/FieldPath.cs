namespace Registrar.Core.Authoring;

// The single owner, for the editor, of the JSON Pointer dialect (RFC 6901, rooted at the
// AGR document) that AuthoringGap.JsonPath and Check.JsonPath both already speak. Nothing
// on the authoring side builds a pointer by hand. The validation layers do build their own,
// which is why FieldPathTests couples these strings to the pointers those layers actually
// emit: the two are not one implementation, so the agreement between them has to be tested
// rather than assumed.
//
// It exists so the rail can navigate by derivation rather than by a registry: an input's
// DOM id is ElementId(its pointer), so there is no map to populate during render and no
// map to drift.
public static class FieldPath
{
    public const string ArtifactType = "/artifactIdentity/artifactType";
    public const string DocumentId = "/artifactIdentity/documentId";
    public const string Version = "/artifactIdentity/version";
    public const string Classification = "/artifactIdentity/classification";
    public const string ApprovalStatus = "/artifactIdentity/approvalStatus";
    public const string Layer = "/artifactIdentity/layer";
    public const string Owner = "/context/owner";
    public const string Audience = "/context/audience";
    public const string PreparedBy = "/context/preparedBy";
    public const string Client = "/context/client";
    public const string Engagement = "/context/engagement";
    public const string ReviewDate = "/context/reviewDate";
    public const string PlaceholderMap = "/dataInputs/placeholderMap";
    public const string SourceWorkflow = "/sourceWorkflowContext";

    public static string Section(int sectionIndex)
        => $"/dataInputs/sections/{sectionIndex}";

    public static string SectionBlocks(int sectionIndex)
        => $"/dataInputs/sections/{sectionIndex}/blocks";

    public static string Block(int sectionIndex, int blockIndex)
        => $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}";

    // Field may itself contain a slash, for example "question/options", because that is
    // how RadioQuestionLayer spells its deepest pointers.
    public static string BlockField(int sectionIndex, int blockIndex, string field)
        => $"{Block(sectionIndex, blockIndex)}/{field}";

    // A pointer always starts with "/", so the "f" prefix keeps the id from starting with
    // the separator, which would be a valid id but an awkward selector.
    public static string ElementId(string jsonPointer)
        => "f" + jsonPointer.Replace('/', '-');
}
