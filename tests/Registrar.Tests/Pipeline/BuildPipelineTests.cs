using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.Tests.Pipeline;

public class BuildPipelineTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    }

    // Validation is a separate stage the pipeline no longer performs, so the test
    // builds the ValidationOutcome the same way a caller (CLI or GUI) would before
    // ever calling BuildPipeline.Run.
    private static (ValidationOutcome Outcome, AgrDocument Agr, EngineContext Engine, byte[] Logo) Inputs(
        string fixtureName)
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var engine = EngineContext.Load(repoRoot);
        var text = SemanticTestSupport.FixtureText(fixtureName);
        var logo = File.ReadAllBytes(
            ConfigLocator.FindAsset(repoRoot, engine.Config.Style.Wordmark.Asset));
        var registry = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        var outcome = new ValidationService(new FixedClock()).Validate(text, engine, registry);
        return (outcome, AgrParser.Parse(text), engine, logo);
    }

    [Fact]
    public void A_passing_fixture_produces_a_document_and_a_manifest()
    {
        var (outcome, agr, engine, logo) = Inputs("agr-sample-ds-draft.json");

        var result = BuildPipeline.Run(outcome, agr, engine, logo);

        Assert.True(result.DocxEligible);
        Assert.NotNull(result.DocxBytes);
        Assert.NotEmpty(result.DocxBytes!);
        Assert.NotNull(result.Manifest);
        Assert.NotNull(result.GenerationLog);
    }

    [Fact]
    public void A_failing_fixture_produces_a_report_and_no_document()
    {
        var (outcome, agr, engine, logo) = Inputs("agr-sample-ds-layer-mismatch.json");

        var result = BuildPipeline.Run(outcome, agr, engine, logo);

        Assert.False(result.DocxEligible);
        Assert.Null(result.DocxBytes);
        Assert.Equal(OverallResult.Fail, result.Report.OverallResult);
    }

    // Finding 6 (whole-branch review): promotion-with-warnings returns no document. No
    // fixture in the repo produces this outcome - the one warning-producing fixture
    // (agr-sample-ds-draft-warn.json, an unrecognized placeholder token) is gated by mode:
    // PlaceholderLayer fails that same condition closed at promotion instead of warning,
    // so it can never reach BuildPipeline as pass-with-warnings in promotion mode. The
    // case is constructed directly by taking a real outcome and setting the two fields
    // BuildPipeline actually branches on (OverallResult, RequestedMode), which is exactly
    // what a caller supplying its own ValidationOutcome would do.
    [Fact]
    public void A_pass_with_warnings_promotion_outcome_produces_no_document()
    {
        var (outcome, agr, engine, logo) = Inputs("agr-sample-ds-draft-warn.json");
        var promotionOutcome = outcome with
        {
            Report = outcome.Report with
            {
                OverallResult = OverallResult.PassWithWarnings,
                RequestedMode = Mode.Promotion
            }
        };

        var result = BuildPipeline.Run(promotionOutcome, agr, engine, logo);

        Assert.False(result.DocxEligible);
        Assert.False(result.ReachedRendering);
        Assert.Null(result.DocxBytes);
        Assert.Null(result.GenerationLog);
        Assert.Null(result.Manifest);
        Assert.Equal(OverallResult.PassWithWarnings, result.Report.OverallResult);
    }

    // Finding 6: post-render-withhold returns a generation log but no document. This is
    // the case BuildCommand's ReachedRendering branch exists for. InputHygieneLayer
    // (QA-ENG-05, pre-render) only lints context fields and the placeholder map, never
    // section body text, so a prohibited character embedded in a paragraph block passes
    // pre-render validation cleanly and is caught only by the post-render QA-SF-08 scan
    // over rendered runs. No fixture exercises this (every prohibited-character fixture
    // targets a pre-render-linted field), so it is constructed here by polluting a clean
    // fixture's body text and re-validating it for real.
    [Fact]
    public void A_prohibited_character_in_body_text_is_withheld_post_render_but_still_logged()
    {
        var (_, agr, engine, logo) = Inputs("agr-sample-ds-draft.json");

        var firstSection = agr.DataInputs.Sections[0];
        // Written as a char-code concatenation, not a literal byte, per SourceHygieneTests.
        var prohibitedText = "Bad " + (char)0x2014 + " dash text.";
        var pollutedBlock = firstSection.Blocks[0] with { Text = prohibitedText };

        var pollutedSection = firstSection with
        {
            Blocks = new[] { pollutedBlock }.Concat(firstSection.Blocks.Skip(1)).ToList()
        };
        var pollutedAgr = agr with
        {
            DataInputs = agr.DataInputs with
            {
                Sections = new[] { pollutedSection }.Concat(agr.DataInputs.Sections.Skip(1)).ToList()
            }
        };

        var repoRoot = SemanticTestSupport.RepoRoot();
        var registry = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        var pollutedOutcome = new ValidationService(new FixedClock())
            .Validate(AgrSerializer.ToJson(pollutedAgr), engine, registry);

        // Confirms the fixture actually exercises the intended gap: pre-render validation
        // is clean, so QA-SF-08 has not fired yet at this point.
        Assert.Equal(OverallResult.Pass, pollutedOutcome.Report.OverallResult);

        var result = BuildPipeline.Run(pollutedOutcome, pollutedAgr, engine, logo);

        Assert.False(result.DocxEligible);
        Assert.True(result.ReachedRendering);
        Assert.Null(result.DocxBytes);
        Assert.Null(result.Manifest);
        Assert.NotNull(result.GenerationLog);
        Assert.Equal(OverallResult.Fail, result.Report.OverallResult);
        Assert.Contains(result.Report.Checks, c => c.CheckId == "QA-SF-08");
    }

    // The pipeline must not touch disk. If it did, a GUI could not use it without a
    // temp directory and every test here would need one.
    [Fact]
    public void The_pipeline_writes_nothing_to_the_fixtures_directory()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var dir = Path.Combine(repoRoot, "fixtures");
        var before = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToList();

        var (outcome, agr, engine, logo) = Inputs("agr-sample-ds-draft.json");
        BuildPipeline.Run(outcome, agr, engine, logo);

        Assert.Equal(before, Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToList());
    }
}
