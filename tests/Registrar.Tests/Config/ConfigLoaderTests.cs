using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Tests.Config;

public class ConfigLoaderTests
{
    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    [Fact]
    public void Load_indexes_artifact_types_by_code()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.True(bundle.ArtifactTypesByCode.ContainsKey("DS"));
        var ds = bundle.ArtifactTypesByCode["DS"];
        Assert.Equal("GOV", ds.Category);
        Assert.Equal("defined", ds.SectionSchema!.Status);
        Assert.Equal("Purpose", ds.SectionSchema.OrderedSections![0]);
        Assert.Contains("CL-006", ds.RequiredClauses!);
    }

    [Fact]
    public void Load_parses_review_intervals_skipping_comment_keys()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal(6, bundle.ReviewIntervalMonthsByCategory["GOV"]);
        Assert.Equal(12, bundle.ReviewIntervalMonthsByCategory["TRN"]);
        Assert.DoesNotContain("$comment", bundle.ReviewIntervalMonthsByCategory.Keys);
    }

    [Fact]
    public void Load_collects_clause_ids_and_canonical_tokens_and_hashes()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("CL-006", bundle.ClauseIds);
        Assert.Contains(bundle.CanonicalTokens, t => t.Token == "[CLIENT ORGANIZATION NAME]");
        Assert.Contains(bundle.CanonicalTokens,
            t => t.Token == "[CLIENT ORGANIZATION NAME]" && t.KnownDriftAliases.Contains("[CLIENT]"));
        Assert.True(bundle.Hashes.ContainsKey("artifact-types.json"));
        Assert.Equal(6, bundle.Hashes.Count);
    }

    [Fact]
    public void Load_parses_approval_status_values_and_transitions()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("Draft", bundle.ApprovalStatuses);
        Assert.Contains("Retired", bundle.ApprovalStatuses);
        Assert.Equal(5, bundle.ApprovalStatuses.Count);

        Assert.Contains("In Review", bundle.Transitions["Draft"]);
        Assert.Contains("Retired", bundle.Transitions["Draft"]);
        Assert.Empty(bundle.Transitions["Superseded"]);
        Assert.Empty(bundle.Transitions["Retired"]);
        Assert.DoesNotContain("$comment", bundle.Transitions.Keys);
    }

    [Fact]
    public void Load_parses_repository_targets_by_category()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal("Controlled Documents/Governance", bundle.RepositoryTargetsByCategory["GOV"]);
        Assert.Equal(6, bundle.RepositoryTargetsByCategory.Count);
        Assert.True(bundle.Hashes.ContainsKey("repository-targets.json"));
    }

    [Fact]
    public void Load_parses_style_rules()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal("#002060", bundle.Style.Colors["headingNavy"]);
        Assert.Equal("org-logo.png", bundle.Style.Wordmark.Asset);
        Assert.Equal(0.55, bundle.Style.Wordmark.HeightIn);
        Assert.Equal(8.5, bundle.Style.Page.WidthIn);
        Assert.Contains("[PACKAGE]", bundle.Style.FooterTemplate);
        Assert.True(bundle.Hashes.ContainsKey("style-rules.json"));
        Assert.Equal(16, bundle.Style.TypeScale.TitlePt);
        Assert.Equal(8, bundle.Style.TypeScale.LabelPt);
    }

    [Fact]
    public void FindAsset_resolves_logo()
    {
        var found = ConfigLocator.FindAsset(AppContext.BaseDirectory, "org-logo.png");
        Assert.True(File.Exists(found));
        Assert.EndsWith("org-logo.png", found.Replace('\\', '/'));
    }

    [Fact]
    public void Load_maps_artifact_type_display_fields()
    {
        var ds = ConfigLoader.Load(RepoRoot()).ArtifactTypesByCode["DS"];

        Assert.Equal("Deployment Summary", ds.Name);
        Assert.Equal("ORG-GOV-DS-NNN", ds.IdPattern);
        Assert.False(string.IsNullOrEmpty(ds.Description));
        Assert.False(string.IsNullOrEmpty(ds.AudienceDefault));
    }

    [Fact]
    public void Load_parses_content_lint_allowlist()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("\u25cb", bundle.ContentLint.ApprovedGlyphAllowlist);
    }

    [Fact]
    public void Exposes_clause_records_by_id()
    {
        var config = ConfigLoader.Load(RepoRoot());
        Assert.True(config.ClausesById.ContainsKey("CL-006"));
        var cl006 = config.ClausesById["CL-006"];
        Assert.False(string.IsNullOrWhiteSpace(cl006.CanonicalText));
        Assert.Matches("^[0-9a-fA-F]{64}$", cl006.CanonicalTextSha256);
    }
}
