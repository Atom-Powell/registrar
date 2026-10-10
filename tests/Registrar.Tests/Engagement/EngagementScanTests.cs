using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Engagement;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Engagement;

public class EngagementScanTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    }

    private static JsonNode Schema()
        => JsonNode.Parse(File.ReadAllText(
            ConfigLocator.FindSchema(SemanticTestSupport.RepoRoot(), "agr.schema.json")))!;

    private static EngagementScanResult Result(string folder)
        => EngagementScan.Run(folder, SemanticTestSupport.Config(), Schema(), new FixedClock());

    private static IReadOnlyList<EngagementRow> Scan(string folder) => Result(folder).Rows;

    // A complete document, written as a .json AGR, is the fixture the CLI already ships.
    private static AgrDocument CompleteDocument()
        => SemanticTestSupport.Agr("agr-sample-ds-draft.json");

    [Fact]
    public void An_empty_folder_yields_no_rows()
    {
        var dir = TempDir.Create();
        try { Assert.Empty(Scan(dir)); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_missing_folder_yields_no_rows_rather_than_throwing()
    {
        var missing = Path.Combine(TempDir.Create(), "does-not-exist");

        var result = Result(missing);

        Assert.Empty(result.Rows);

        // A mistyped client name must not read as "this client owes nothing", which is
        // what an empty row list alone said.
        Assert.Equal(EngagementFolderState.NotFound, result.State);
        Assert.Contains(missing, result.Message, StringComparison.Ordinal);
    }

    // The pair the screen has to tell apart: both yield no rows, and only one of them
    // means the client owes nothing.
    [Fact]
    public void An_existing_empty_folder_is_distinguishable_from_a_missing_one()
    {
        var dir = TempDir.Create();
        try
        {
            var empty = Result(dir);
            var missing = Result(Path.Combine(dir, "does-not-exist"));

            Assert.Empty(empty.Rows);
            Assert.Empty(missing.Rows);

            Assert.Equal(EngagementFolderState.Empty, empty.State);
            Assert.Equal(EngagementFolderState.NotFound, missing.State);
            Assert.NotEqual(empty.Message, missing.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The screen opens in this state, before any folder has been typed. Directory.Exists
    // is false for an empty string, so without this it would report NotFound and name a
    // path that is not there.
    [Fact]
    public void No_folder_supplied_is_its_own_state_rather_than_a_missing_folder()
    {
        var result = Result(string.Empty);

        Assert.Empty(result.Rows);
        Assert.Equal(EngagementFolderState.NotSpecified, result.State);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void A_folder_holding_artifacts_reports_populated()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(CompleteDocument()));

            Assert.Equal(EngagementFolderState.Populated, Result(dir).State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The rule AgrPicker gets wrong. All four output names, and nothing else.
    [Fact]
    public void Build_output_files_are_not_listed_as_artifacts()
    {
        var dir = TempDir.Create();
        try
        {
            foreach (var name in new[]
            {
                "validation-report.json",
                "artifact-manifest.json",
                "generation-log.json",
                "promotion-payload.json"
            })
            {
                File.WriteAllText(Path.Combine(dir, name), "{\"checks\": []}");
            }

            Assert.Empty(Scan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_json_file_without_artifactIdentity_is_not_listed()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.json"), "{\"something\": \"else\"}");

            Assert.Empty(Scan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Kept rather than excluded: the structural test cannot run, so it must be shown.
    [Fact]
    public void A_json_file_too_malformed_to_classify_is_listed_as_unreadable()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json at all");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
            Assert.Equal("broken.json", row.FileName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_corrupt_draft_is_listed_as_unreadable()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "wip" + DraftFile.Extension), "{ broken");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_incomplete_draft_is_listed_as_draft_and_says_what_is_missing()
    {
        var dir = TempDir.Create();
        try
        {
            DraftFile.Write(
                Path.Combine(dir, "wip" + DraftFile.Extension),
                new AgrDraft { ArtifactType = "DS" });

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Draft, row.State);
            Assert.Equal("DS", row.ArtifactType);
            Assert.NotEmpty(row.Detail);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_complete_artifact_with_no_docx_is_ready()
    {
        var dir = TempDir.Create();
        try
        {
            var document = CompleteDocument();
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(document));

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Ready, row.State);
            Assert.Equal("DS", row.ArtifactType);
            Assert.Equal(document.Identity.DocumentId, row.DocumentId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_complete_artifact_whose_docx_exists_is_built()
    {
        var dir = TempDir.Create();
        try
        {
            var document = CompleteDocument();
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(document));
            File.WriteAllText(
                Path.Combine(dir, document.RenderingOptions.OutputFileName), "not a real docx");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Built, row.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Every_row_carries_a_detail_line()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(CompleteDocument()));
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ nope");
            DraftFile.Write(Path.Combine(dir, "wip" + DraftFile.Extension), new AgrDraft());

            var rows = Scan(dir);

            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Detail)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rows_are_ordered_by_file_name_so_the_screen_is_stable()
    {
        var dir = TempDir.Create();
        try
        {
            foreach (var name in new[] { "c.json", "a.json", "b.json" })
                File.WriteAllText(Path.Combine(dir, name), AgrSerializer.ToJson(CompleteDocument()));

            var rows = Scan(dir);

            Assert.Equal(new[] { "a.json", "b.json", "c.json" }, rows.Select(r => r.FileName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The .json path used to read the file twice: once to classify it, once to parse
    // it. A locked file fails both reads the same way, so a guard on only the first
    // read let the second escape uncaught.
    [Fact]
    public void A_locked_json_file_is_listed_as_unreadable_rather_than_throwing()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "locked.json");
            File.WriteAllText(path, AgrSerializer.ToJson(CompleteDocument()));

            using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
            Assert.Equal("locked.json", row.FileName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // An ordinary condition in a synced SharePoint folder, the deployment target.
    // FileSystemAccessRule and friends are Windows-only APIs; this repo's dev and
    // runtime target is Windows 11 exclusively, so the CA1416 platform-compatibility
    // warning is suppressed locally for this one test rather than avoided by not
    // testing the case at all.
#pragma warning disable CA1416
    [Fact]
    public void A_permission_denied_json_file_is_listed_as_unreadable_rather_than_throwing()
    {
        var dir = TempDir.Create();
        var path = Path.Combine(dir, "denied.json");
        File.WriteAllText(path, AgrSerializer.ToJson(CompleteDocument()));

        var fileInfo = new FileInfo(path);
        var acl = fileInfo.GetAccessControl();
        var user = WindowsIdentity.GetCurrent()!.User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.ReadData, AccessControlType.Deny);
        acl.AddAccessRule(rule);
        fileInfo.SetAccessControl(acl);

        try
        {
            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
            Assert.Equal("denied.json", row.FileName);
        }
        finally
        {
            acl.RemoveAccessRule(rule);
            fileInfo.SetAccessControl(acl);
            Directory.Delete(dir, recursive: true);
        }
    }
#pragma warning restore CA1416

    // DraftFile.Write can never produce this: AgrDraft.Sections defaults to an empty
    // list, and that default drops out only when the JSON omits the property, not when
    // it is present and explicitly null. Written directly to reach the case a
    // hand-edited or truncated file can still produce.
    [Fact]
    public void A_draft_with_explicit_null_sections_is_listed_as_unreadable()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "wip" + DraftFile.Extension),
                "{\"ArtifactType\": \"DS\", \"Sections\": null}");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
