using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class RegistryFormatException : Exception
{
    public RegistryFormatException(string message) : base(message) { }
}

public sealed class CsvIdRegistry : IIdRegistry
{
    private static readonly string[] Required =
        { "documentid", "artifacttype", "classification", "approvalstatus", "version" };

    private readonly IReadOnlyDictionary<string, RegistryRecord> _byId;

    private CsvIdRegistry(IReadOnlyDictionary<string, RegistryRecord> byId) => _byId = byId;

    public RegistryRecord? Lookup(string documentId)
        => _byId.TryGetValue(documentId, out var record) ? record : null;

    public IReadOnlyCollection<RegistryRecord> All() =>
        _byId.Values.OrderBy(r => r.DocumentId, StringComparer.Ordinal).ToList();

    public static CsvIdRegistry FromCsvText(string csvText)
    {
        var rows = Csv.Parse(csvText);
        if (rows.Count == 0)
            throw new RegistryFormatException("Registry export is empty (no header row).");

        var index = new Dictionary<string, int>();
        var header = rows[0];
        for (var i = 0; i < header.Count; i++)
            index[header[i].Trim().ToLowerInvariant()] = i;

        foreach (var col in Required)
            if (!index.ContainsKey(col))
                throw new RegistryFormatException($"Registry export is missing the required column '{col}'.");

        var byId = new Dictionary<string, RegistryRecord>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            if (row.Count < header.Count)
                throw new RegistryFormatException(
                    $"Registry export has a malformed row with {row.Count} columns (expected at least {header.Count}).");

            string Field(string col) => row[index[col]].Trim();

            var record = new RegistryRecord(
                Field("documentid"), Field("artifacttype"), Field("classification"),
                Field("approvalstatus"), Field("version"));

            if (string.IsNullOrEmpty(record.DocumentId))
                throw new RegistryFormatException("Registry export has a row with a blank Document ID.");
            if (byId.ContainsKey(record.DocumentId))
                throw new RegistryFormatException($"Registry export has a duplicate Document ID '{record.DocumentId}'.");
            byId[record.DocumentId] = record;
        }

        return new CsvIdRegistry(byId);
    }
}
