using Registrar.Validation;

namespace Registrar.Tests;

public class CsvIdRegistryTests
{
    private const string Sample =
        "documentId,artifactType,classification,approvalStatus,version,client\n" +
        "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0,\"Talewind CPA, LLC\"\n" +
        "ORG-GOV-DS-017,DS,Client-Facing,Retired,v2.0,\"Acme, Inc\"\n";

    [Fact]
    public void Lookup_returns_record_ignoring_extra_columns()
    {
        var registry = CsvIdRegistry.FromCsvText(Sample);

        var record = registry.Lookup("ORG-GOV-DS-016");
        Assert.NotNull(record);
        Assert.Equal("DS", record!.ArtifactType);
        Assert.Equal("Client-Facing", record.Classification);
        Assert.Equal("Draft", record.ApprovalStatus);
        Assert.Equal("v1.0", record.Version);
    }

    [Fact]
    public void Lookup_returns_null_for_absent_id()
    {
        Assert.Null(CsvIdRegistry.FromCsvText(Sample).Lookup("ORG-GOV-DS-999"));
    }

    [Fact]
    public void FromCsvText_throws_on_missing_required_column()
    {
        var csv = "documentId,artifactType,classification,version\nORG-GOV-DS-016,DS,Client-Facing,v1.0\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }

    [Fact]
    public void FromCsvText_throws_on_duplicate_document_id()
    {
        var csv = Sample + "ORG-GOV-DS-016,DS,Client-Facing,Approved,v3.0,\"Dup\"\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }

    [Fact]
    public void FromCsvText_throws_on_row_with_blank_document_id()
    {
        var csv = "documentId,artifactType,classification,approvalStatus,version\n" +
                  ",DS,Client-Facing,Draft,v1.0\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }

    [Fact]
    public void FromCsvText_throws_on_ragged_row()
    {
        var csv = "documentId,artifactType,classification,approvalStatus,version\n" +
                  "ORG-GOV-DS-016,DS\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }

    [Fact]
    public void FromCsvText_still_skips_fully_blank_trailing_line()
    {
        var registry = CsvIdRegistry.FromCsvText(Sample + "\n");
        Assert.NotNull(registry.Lookup("ORG-GOV-DS-016"));
    }

    [Fact]
    public void All_returns_every_row_in_the_export()
    {
        var csv = "documentId,artifactType,classification,approvalStatus,version\n"
            + "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0\n"
            + "ORG-TRN-TCL-004,TCL,Client-Facing,Draft,v1.0\n";

        var all = CsvIdRegistry.FromCsvText(csv).All();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.DocumentId == "ORG-GOV-DS-016");
        Assert.Contains(all, r => r.DocumentId == "ORG-TRN-TCL-004");
    }
}
