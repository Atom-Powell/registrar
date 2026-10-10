using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core.Engagement;

// The on-disk form of a work-in-progress artifact.
//
// The extension is deliberately not ".json". AgrPicker globs "*.json" to build the
// CLI's operator menu, so a draft ending in .json would appear there as a selectable
// AGR. A distinct extension keeps drafts invisible to it without changing AgrPicker.
//
// Read and Write live together so the format is defined once. Nothing writes a draft
// in this unit; GUI-7 does. Write exists now because a format first defined inside a
// test drifts the moment production code writes one.
public static class DraftFile
{
    public const string Extension = ".agrdraft";

    public static void Write(string path, AgrDraft draft)
        => File.WriteAllText(path, JsonSerializer.Serialize(draft, AgrSerializer.Options));

    // Returns null rather than throwing for every failure a folder scan can meet: the
    // file is gone, it is not JSON, or it is JSON of the wrong shape. The caller turns
    // null into an Unreadable row, which is what the operator needs to see.
    public static AgrDraft? Read(string path)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<AgrDraft>(File.ReadAllText(path), AgrSerializer.Options);

            // Sections and PlaceholderMap default to an empty collection, but that
            // default applies only when the property is absent. JSON that sets either
            // explicitly to null overwrites the default with null, and both properties
            // are declared non-nullable, so a null in either is a draft that violates
            // its own model. A draft this unit writes can never look like this, but
            // tolerating a corrupt draft is exactly what this unit exists to do, so a
            // hand-edited or truncated file can still produce it.
            //
            // The two fail for different reasons and both are worth failing on.
            // AgrAuthoring.ToDocument enumerates Sections with no null check, so a null
            // there throws an unguarded NullReferenceException and takes the scan down.
            // PlaceholderMap is not enumerated: ToDocument assigns it straight through
            // to AgrDocument.DataInputs.PlaceholderMap, which is declared "required" and
            // non-nullable. So a null there does not crash the scan; it manufactures a
            // document whose contract says the map cannot be null while it is, and hands
            // that to every downstream consumer, PlaceholderLayer among them. Failing
            // here keeps the file Unreadable in both cases. Narrowing the guard to
            // Sections would trade a loud failure for a quiet one; silently substituting
            // an empty collection would repair a corrupt file without telling anyone.
            if (draft is not null && (draft.Sections is null || draft.PlaceholderMap is null))
                return null;

            return draft;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
