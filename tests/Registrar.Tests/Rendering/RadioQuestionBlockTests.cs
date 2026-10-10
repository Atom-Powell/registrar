using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Core.Model;
using Registrar.Rendering;
using Xunit;

namespace Registrar.Tests.Rendering;

public class RadioQuestionBlockTests
{
    private static AgrQuestion Question(bool required = true) => new()
    {
        Number = 3,
        Prompt = "Was the prompting walkthrough completed?",
        Required = required,
        Options = new[]
        {
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
        }
    };

    private static List<Paragraph> Render(AgrQuestion q)
    {
        var config = SemanticTestSupport.Config();
        return RadioQuestionBlock.Render(q, config.Style, config.RadioPattern).ToList();
    }

    [Fact]
    public void Renders_a_descriptor_line_then_one_paragraph_per_option()
    {
        var paras = Render(Question());

        Assert.Equal(5, paras.Count); // 1 descriptor + 4 options
        Assert.Equal("Question 3. Select one answer. Required", paras[0].InnerText);
    }

    [Fact]
    public void Descriptor_line_uses_the_optional_label_when_not_required()
    {
        var paras = Render(Question(required: false));

        Assert.Equal("Question 3. Select one answer. Optional", paras[0].InnerText);
    }

    [Fact]
    public void Descriptor_line_is_italic_and_secondary_gray()
    {
        var paras = Render(Question());

        var rpr = paras[0].Descendants<RunProperties>().First();
        Assert.NotNull(rpr.Italic);
        Assert.Equal("5A6470", rpr.Color!.Val!.Value);
    }

    [Fact]
    public void Each_option_leads_with_the_radio_glyph_in_the_configured_color()
    {
        var paras = Render(Question());

        var option = paras[1];
        var runs = option.Descendants<Run>().ToList();
        Assert.Equal("\u25cb", runs[0].InnerText); // U+25CB
        Assert.Equal("005CB9", runs[0].RunProperties!.Color!.Val!.Value);
        Assert.Contains("Yes, reviewed and acknowledged", option.InnerText);
    }

    [Fact]
    public void Options_are_indented()
    {
        var paras = Render(Question());

        var ind = paras[1].ParagraphProperties!.Indentation;
        Assert.NotNull(ind);
        Assert.True(int.Parse(ind!.Left!.Value!) > 0);
    }

    // QA-SF-10 forbids checkboxes, plain bullets, dash lists, and numbered lists for
    // selections. RadioQuestionBlock is the only path that emits a radio question, so
    // the guarantee is structural: it references no numbering definition at all.
    [Fact]
    public void QA_SF_10_emits_no_numbering_reference()
    {
        var paras = Render(Question());

        Assert.All(paras, p => Assert.Empty(p.Descendants<NumberingProperties>()));
    }
}
