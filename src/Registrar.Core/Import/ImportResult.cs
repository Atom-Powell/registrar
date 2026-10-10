using Registrar.Core.Model;

namespace Registrar.Core.Import;

// What a paste produced. Draft is null only when no JSON could be recovered at all; a
// paste that recovers a draft returns it even when findings accompany it, because the
// editor keeps showing the problems and Build stays blocked until they are resolved.
public sealed record ImportResult(
    AgrDraft? Draft,
    IReadOnlyList<ImportFinding> Findings);

// Deliberately not a Check. A Check carries checkId, ruleSource, and errorCategory, none
// of which mean anything for "this paste was truncated", and dressing a transport problem
// as a governance violation would mislead both readers of a validation report. This
// mirrors the reasoning behind AuthoringGap.
public sealed record ImportFinding(
    string? JsonPath,
    string Message,
    ImportFindingKind Kind);

public enum ImportFindingKind
{
    // Nothing in the paste was an attempt at JSON: either no brace appeared anywhere, or
    // the only spans carrying one came from raw prose rather than a code fence and held no
    // quotation mark, which no JSON object with content can manage.
    NoJsonFound,

    // A JSON-shaped span existed but would not parse. This is what a fenced envelope whose
    // delimiters a chat surface replaced with curly quotes reports as, and the case the
    // design records as the one Normalize deliberately cannot fix.
    Malformed,

    // Braces or brackets never closed, the signature of a reply cut off by a length limit.
    Truncated,

    // A character the engine prohibits, found in the recovered payload.
    ProhibitedCharacter,

    // A member was present and non-null but the deserializer refused it, so its content
    // was discarded. Deliberately not Malformed: that kind means the whole paste could not
    // be read, and reusing it would be ambiguous on a result that also carries a draft.
    // An absent member is not Unreadable; AuthoringGap already reports those.
    Unreadable,

    // An identity field (ArtifactType, DocumentId, Owner, Client) in the imported draft
    // disagrees with the open draft. The engine does not apply identity fields from a paste
    // because Registry Owner issues Document IDs and the operator chose which artifact
    // to open. A disagreement is reported so the operator can verify they pasted the correct
    // reply, but the field is not applied. This is distinct from Unreadable: deserialization
    // succeeded, and the value was deliberately refused by business rule, not by parsing.
    IdentityConflict
}

// Text is the repaired AGR envelope, ready to hand back to Parse. The conversational
// wrapper is dropped, because repair works on the parsed payload and re-serializes it.
//
// Collisions is non-empty exactly when repair was refused: two or more source keys in the
// same object would have repaired to the same key, and JsonObject's indexer is add-or-update,
// so applying the repair would have silently dropped one of them. When Collisions is
// non-empty, Text is the original pastedText, unchanged, and Repaired is 0. Repair is
// all-or-nothing; a partial repair that quietly lost a placeholder would be worse than no
// repair, because the operator would believe the document had been cleaned.
public sealed record NormalizeResult(string Text, int Repaired, IReadOnlyList<string> Collisions);
