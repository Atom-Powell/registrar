using Registrar.Core.Model;

namespace Registrar.Validation;

// The authoritative ID Registry, swappable behind this interface. Slice 3 ships
// a CSV-export adapter (CsvIdRegistry); an xlsx or SharePoint adapter slots in
// later without touching the layers that consume it.
public interface IIdRegistry
{
    RegistryRecord? Lookup(string documentId);

    // Read-only. Rule 5 stands: the engine never mints an ID. Enumerating what
    // Registry Owner already issued is not minting, and it is what lets an
    // operator pick an ID instead of typing one.
    IReadOnlyCollection<RegistryRecord> All();
}
