using Registrar.Core.Engagement;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Engagement;

public class DraftFileTests
{
    [Fact]
    public void The_extension_keeps_drafts_out_of_the_json_glob()
    {
        // AgrPicker globs *.json. A draft must not match it.
        Assert.Equal(".agrdraft", DraftFile.Extension);
        Assert.False(DraftFile.Extension.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_draft_survives_a_write_then_read()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "sample" + DraftFile.Extension);
            var original = new AgrDraft
            {
                ArtifactType = "DS",
                DocumentId = "ORG-GOV-DS-016",
                Client = "Talewind CPA",
                Owner = "Registry Owner"
            };

            DraftFile.Write(path, original);
            var read = DraftFile.Read(path);

            Assert.NotNull(read);
            Assert.Equal("DS", read!.ArtifactType);
            Assert.Equal("ORG-GOV-DS-016", read.DocumentId);
            Assert.Equal("Talewind CPA", read.Client);
            Assert.Equal("Registry Owner", read.Owner);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_corrupt_draft_reads_as_null_rather_than_throwing()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "broken" + DraftFile.Extension);
            File.WriteAllText(path, "{ this is not json");

            Assert.Null(DraftFile.Read(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_draft_holding_valid_json_that_is_not_an_object_reads_as_null()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "array" + DraftFile.Extension);
            File.WriteAllText(path, "[1, 2, 3]");

            Assert.Null(DraftFile.Read(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Both properties are declared non-nullable and an explicit JSON null defeats their
    // defaults. Sections would crash AgrAuthoring.ToDocument outright; PlaceholderMap
    // would pass silently into AgrDocument's required, non-nullable placeholderMap. Both
    // are a draft violating its own model, so both read as null here.
    [Theory]
    [InlineData("{\"ArtifactType\": \"DS\", \"Sections\": null}")]
    [InlineData("{\"ArtifactType\": \"DS\", \"PlaceholderMap\": null}")]
    public void A_draft_setting_a_non_nullable_collection_to_null_reads_as_null(string json)
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "hand-edited" + DraftFile.Extension);
            File.WriteAllText(path, json);

            Assert.Null(DraftFile.Read(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_reads_as_null()
    {
        Assert.Null(DraftFile.Read(Path.Combine(TempDir.Create(), "absent" + DraftFile.Extension)));
    }
}
