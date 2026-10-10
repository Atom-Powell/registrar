using System;
using System.Collections.Generic;
using System.Linq;
using Registrar.Core;
using Registrar.Core.Config;
using Xunit;

namespace Registrar.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Loads_radio_option_pattern_from_style_rules()
    {
        var config = SemanticTestSupport.Config();

        Assert.Equal("\u25cb", config.RadioPattern.Glyph); // U+25CB, escapes-only in source
        Assert.Equal("#005CB9", config.RadioPattern.GlyphColor);
        Assert.True(config.RadioPattern.OptionsIndented);
        Assert.Equal(360, config.RadioPattern.OptionIndentTwips);
        Assert.Equal(4, config.RadioPattern.MinimumOptions);
        Assert.Equal("Question {number}. Select one answer. {requirement}",
            config.RadioPattern.DescriptorLineFormat);
        Assert.Equal("Required", config.RadioPattern.RequiredLabel);
        Assert.Equal("Optional", config.RadioPattern.OptionalLabel);
        Assert.Equal(4, config.RadioPattern.StandardOptionSet.Count);
    }

    [Theory]
    [InlineData("DS", "Governance")]
    [InlineData("AUP", "Governance")]
    [InlineData("UQR", "Delivery")]
    [InlineData("DCN", "Delivery")]
    [InlineData("TCL", "Delivery")]
    [InlineData("TAK", "Delivery")]
    [InlineData("RAF", "Operational")]
    [InlineData("PIG", "Administration")]
    [InlineData("SCL", "Administration")]
    [InlineData("PDX", "Administration")]
    public void Generable_types_declare_their_framework_layer(string code, string expected)
    {
        var config = SemanticTestSupport.Config();

        Assert.Equal(expected, config.ArtifactTypesByCode[code].Layer);
    }

    [Fact]
    public void Types_without_a_defined_section_schema_declare_no_layer()
    {
        var config = SemanticTestSupport.Config();

        foreach (var type in config.ArtifactTypesByCode.Values)
            if (type.SectionSchema?.Status != "defined")
                Assert.Null(type.Layer);
    }

    [Fact]
    public void Types_with_a_defined_section_schema_declare_a_layer()
    {
        var config = SemanticTestSupport.Config();

        foreach (var type in config.ArtifactTypesByCode.Values)
            if (type.SectionSchema?.Status == "defined")
                Assert.False(string.IsNullOrEmpty(type.Layer),
                    $"{type.Code} has a defined section schema but no configured layer.");
    }

    // Finding 1: the Governor's scope is eight types, narrower than the ten with a
    // defined section schema. SCL and PDX are generable, engine-rendered master-pack
    // reference documents but are not client-engagement artifacts the Governor may be
    // asked to produce.
    [Fact]
    public void Exactly_eight_types_carry_governor_scope()
    {
        var config = SemanticTestSupport.Config();

        var inScope = config.ArtifactTypesByCode.Values
            .Where(t => t.GovernorScope)
            .Select(t => t.Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var expected = new[] { "AUP", "DCN", "DS", "PIG", "RAF", "TAK", "TCL", "UQR" }
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, inScope);
    }

    // The flag can never be set on a type the engine cannot render: GovernorScope must
    // imply a defined section schema.
    [Fact]
    public void Every_governor_scope_type_has_a_defined_section_schema()
    {
        var config = SemanticTestSupport.Config();

        foreach (var type in config.ArtifactTypesByCode.Values)
            if (type.GovernorScope)
                Assert.Equal("defined", type.SectionSchema?.Status);
    }

    [Fact]
    public void Vocabulary_is_bound_from_vocabularies_json()
    {
        var v = SemanticTestSupport.Config().Vocabulary;

        Assert.StartsWith("^ORG-", v.DocumentIdPattern);
        Assert.Equal("^v[0-9]+\\.[0-9]+$", v.VersionPattern);
        Assert.Contains("Client-Facing", v.Classifications);
        Assert.Contains("Controlled Signoff", v.Classifications);
        Assert.Contains("Internal Working Document", v.Classifications);
        Assert.Equal(4, v.FrameworkLayers.Count);
        Assert.Contains("Governance", v.FrameworkLayers);
        Assert.Contains("GOV", v.Categories);
        Assert.Contains("Final", v.ProhibitedStatusAliases);
        Assert.Equal("Draft", v.InitialState);
        Assert.Equal("v1.0", v.InitialVersion);
    }

    [Fact]
    public void Radio_option_pattern_flag_is_bound_per_artifact_type()
    {
        var types = SemanticTestSupport.Config().ArtifactTypesByCode;

        Assert.True(types["TCL"].UsesRadioOptionPattern);
        Assert.True(types["DQR"].UsesRadioOptionPattern);
        Assert.False(types["DS"].UsesRadioOptionPattern);
    }

    [Fact]
    public void Qa_catalog_binds_rule_text_and_enumerates()
    {
        var qa = SemanticTestSupport.Config().Qa;

        Assert.NotEmpty(qa.Entries);
        Assert.Equal(qa.Entries.Select(e => e.CheckId).OrderBy(id => id, StringComparer.Ordinal),
            qa.Entries.Select(e => e.CheckId));

        var eng08 = qa.Entries.Single(e => e.CheckId == "QA-ENG-08");
        Assert.Equal("input", eng08.Layer);
        Assert.Equal("MetadataViolation", eng08.ErrorCategory);
        Assert.False(string.IsNullOrWhiteSpace(eng08.Rule));
        Assert.Contains("artifactIdentity.layer", eng08.Rule);
    }

    [Fact]
    public void The_screen_block_binds_its_colors()
    {
        var screen = SemanticTestSupport.Config().Screen;

        Assert.Equal("#A3282B", screen.Colors["danger"]);
        Assert.Equal("#7E8B9C", screen.Colors["border"]);
    }

    [Fact]
    public void The_screen_block_carries_only_the_four_colors_it_needs()
    {
        var screen = SemanticTestSupport.Config().Screen;

        // Two brand values were approved for the window (danger, border), and two more for
        // the ORG branding of 2026-10-08 (surface, dangerHover). A fifth arriving without a
        // ratified decision is the drift this assertion exists to catch.
        Assert.Equal(4, screen.Colors.Count);
    }

    [Fact]
    public void The_document_palette_is_untouched_by_the_screen_block()
    {
        var config = SemanticTestSupport.Config();

        // The renderer reads visualIdentity. If a screen token ever leaks into it, a
        // document starts rendering a color that was chosen for a monitor.
        Assert.DoesNotContain("danger", config.Style.Colors.Keys);
        Assert.DoesNotContain("border", config.Style.Colors.Keys);

        // The count matters as much as the two names. Asserting only known keys would
        // let any other leak through, and "border" in particular is a name the document
        // palette could plausibly grow on its own.
        Assert.Equal(10, config.Style.Colors.Count);

        Assert.Equal("#C0504D", config.Style.Colors["warnRedBorder"]);
        Assert.Equal("#B8C1CC", config.Style.Colors["borderNeutral"]);
    }

    [Fact]
    public void Transport_repairs_parse_to_code_points()
    {
        var repairs = SemanticTestSupport.Config().ContentLint.ParseTransportRepairs();

        Assert.Equal("'", repairs[0x2019]);   // right single quotation mark
        Assert.Equal("-", repairs[0x2014]);   // em dash
        Assert.Equal("...", repairs[0x2026]); // horizontal ellipsis
        Assert.Equal(" ", repairs[0x00A0]);   // no-break space
        Assert.Equal("", repairs[0x00AD]);    // soft hyphen
    }

    [Fact]
    public void A_transport_repair_comment_key_is_not_a_repair()
    {
        var repairs = SemanticTestSupport.Config().ContentLint.ParseTransportRepairs();

        Assert.All(repairs.Keys, cp => Assert.True(cp > 0));
        Assert.DoesNotContain(repairs, r => r.Value.Contains("READ BY THE ENGINE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_transport_repair_key_fails_closed()
    {
        var lint = new ContentLint
        {
            ApprovedGlyphAllowlist = Array.Empty<string>(),
            TransportRepairs = new Dictionary<string, string> { ["2019"] = "'" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => lint.ParseTransportRepairs());
        Assert.Contains("2019", ex.Message, StringComparison.Ordinal);
    }

    // Only the literal "$comment" is the file's own annotation. Skipping every key that
    // merely starts with "$" would let a typo such as "$2019" pass as a comment, which is
    // the silent skip the fail-closed rule three lines above exists to prevent.
    [Fact]
    public void A_dollar_prefixed_key_that_is_not_a_comment_still_fails_closed()
    {
        var lint = new ContentLint
        {
            ApprovedGlyphAllowlist = Array.Empty<string>(),
            TransportRepairs = new Dictionary<string, string> { ["$2019"] = "'" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => lint.ParseTransportRepairs());
        Assert.Contains("$2019", ex.Message, StringComparison.Ordinal);
    }

    // The load-time gate the spec calls for. A config release carrying a malformed repair
    // key must be rejected where every other config defect is rejected, at load, rather
    // than loading cleanly and then crashing whichever code path happens to read the map
    // first.
    [Fact]
    public void ConfigLoader_fails_closed_on_a_malformed_transport_repair_key()
    {
        var root = ConfigTestSupport.CopyConfigTree(
            styleRules => styleRules.Replace(
                "\"U+2019\": \"'\"", "\"2019\": \"'\"", StringComparison.Ordinal));

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(root));
        Assert.Contains("2019", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigBundle_exposes_the_parsed_transport_repairs()
    {
        var repairs = SemanticTestSupport.Config().TransportRepairs;

        Assert.Equal("'", repairs[0x2019]);
        Assert.Equal("-", repairs[0x2014]);
        Assert.Equal("...", repairs[0x2026]);
    }

    [Fact]
    public void Every_repaired_code_point_is_actually_prohibited()
    {
        var config = SemanticTestSupport.Config();
        var policy = CharacterPolicy.FromConfig(config.ContentLint);

        foreach (var (codePoint, replacement) in config.ContentLint.ParseTransportRepairs())
        {
            Assert.False(policy.IsAllowed(new System.Text.Rune(codePoint)),
                $"U+{codePoint:X4} has a repair entry but is already allowed, so the entry is dead config.");

            // The other half of the same guarantee. A replacement that itself carried a
            // prohibited character would repair one offense into another, and Normalize
            // would stop being idempotent: a second pass would keep finding work to do.
            Assert.Empty(CharacterLint.Scan(replacement, policy));
        }
    }
}
