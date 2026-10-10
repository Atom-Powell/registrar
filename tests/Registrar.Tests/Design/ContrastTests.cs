using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Design;

public class ContrastTests
{
    private const string White = "#FFFFFF";

    // WCAG 2.1 minimums. Text needs 4.5:1; a control boundary that carries no text needs
    // 3:1. Named rather than inlined so a failure message can say which rule was missed.
    private const double TextMinimum = 4.5;
    private const double BoundaryMinimum = 3.0;

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#000000", "#000000", 1.0)]
    public void The_ratio_matches_the_WCAG_reference_values(string a, string b, double expected)
    {
        Assert.Equal(expected, Contrast.Ratio(a, b), 2);
    }

    [Fact]
    public void The_ratio_is_symmetric()
    {
        Assert.Equal(Contrast.Ratio("#005CB9", White), Contrast.Ratio(White, "#005CB9"), 10);
    }

    // Pins each luminance coefficient individually. The black-and-white reference values
    // above cannot do this: both collapse the gamma curve to a fixed point and the three
    // weights sum to 1, so they stay correct even if the coefficients were swapped
    // between channels. A pure primary isolates one weight, which is the only way a
    // transposition shows up as a failure.
    [Theory]
    [InlineData("#FF0000", 0.2126)]
    [InlineData("#00FF00", 0.7152)]
    [InlineData("#0000FF", 0.0722)]
    public void Each_luminance_coefficient_is_pinned_by_its_pure_primary(string hex, double expected)
    {
        Assert.Equal(expected, Contrast.RelativeLuminance(hex), 6);
    }

    // Pins the gamma transform itself. Every other color this suite asserts on is a fixed
    // point of that curve: Channel(1.0) is 1.0 for any exponent and any offset pair, and
    // Channel(0.0) is 0.0 for any divisor. So the 21.0 reference, the pure primaries, and
    // the whole role table below stay green even if the exponent is changed from 2.4 to
    // 2.2. A mid-tone is the cheapest color that is not a fixed point.
    //
    // #777777 puts every channel at 119/255 = 0.466666..., which is above the 0.03928
    // branch threshold, so it takes the power branch. ((119/255) + 0.055) / 1.055 raised
    // to 2.4 is 0.184474994500441, and because the three coefficients sum to 1 and all
    // three channels are equal, the relative luminance is that same number. At exponent
    // 2.2 the same input yields 0.212377928557012, so six decimal places separate them by
    // a wide margin and also pin the 0.055 and 1.055 offsets.
    [Fact]
    public void A_mid_tone_pins_the_gamma_exponent_and_its_offsets()
    {
        Assert.Equal(0.184475, Contrast.RelativeLuminance("#777777"), 6);
    }

    // Pins the linear branch, which the mid-tone above cannot reach and which pure black
    // cannot distinguish because zero divided by anything is zero. #050505 puts every
    // channel at 5/255 = 0.019607..., below the 0.03928 threshold, so each channel is
    // 5/255 divided by 12.92 and the weighted sum is that same value. This fails if the
    // divisor changes, and it fails if the branch threshold moves far enough to send this
    // color down the power branch, independently of the coefficients.
    [Fact]
    public void A_near_black_pins_the_linear_branch_divisor_and_threshold()
    {
        Assert.Equal(5.0 / 255.0 / 12.92, Contrast.RelativeLuminance("#050505"), 12);
    }

    // The blanket rule. Every screen color must be at least visible as a boundary, so a
    // token added later without a role entry below is still caught by something.
    [Fact]
    public void Every_screen_color_is_at_least_visible_on_white()
    {
        foreach (var (name, hex) in SemanticTestSupport.Config().Screen.Colors)
        {
            // surface is white itself, so it is 1:1 on white by definition. It is a fill
            // and a text color on danger, never a boundary; the role table pins both pairings.
            if (name == "surface") continue;

            var ratio = Contrast.Ratio(hex, White);
            Assert.True(ratio >= BoundaryMinimum,
                $"screen.colors.{name} ({hex}) measures {ratio:F2}:1 on white, "
                + $"below the {BoundaryMinimum}:1 control-boundary minimum.");
        }
    }

    // The role table. Each pair is a place the GUI actually puts one color on another.
    [Fact]
    public void Every_screen_color_meets_the_threshold_for_the_role_it_serves()
    {
        var config = SemanticTestSupport.Config();
        var screen = config.Screen.Colors;
        var document = config.Style.Colors;

        var roles = new (string Label, string Foreground, string Background, double Minimum)[]
        {
            ("danger text on white", screen["danger"], White, TextMinimum),
            ("danger text on the warn fill", screen["danger"], document["warnRedFill"], TextMinimum),
            ("surface text on the danger fill", screen["surface"], screen["danger"], TextMinimum),
            ("surface text on the danger hover fill", screen["surface"], screen["dangerHover"], TextMinimum),
            ("control boundary on white", screen["border"], White, BoundaryMinimum),
            ("primary blue text on white", document["primaryBlue"], White, TextMinimum),
            ("heading navy text on white", document["headingNavy"], White, TextMinimum),
            ("accent blue text on white", document["accentBlue"], White, TextMinimum),
            ("secondary gray text on white", document["secondaryGray"], White, TextMinimum),
            ("primary blue text on the DCH fill", document["primaryBlue"], document["dchFill"], TextMinimum),
            ("primary blue text on the header right cell fill", document["primaryBlue"], document["headerRightCellFill"], TextMinimum),
            ("heading navy text on the table row alt fill", document["headingNavy"], document["tableRowAltFill"], TextMinimum)
        };

        foreach (var (label, fg, bg, minimum) in roles)
        {
            var ratio = Contrast.Ratio(fg, bg);
            Assert.True(ratio >= minimum,
                $"{label}: {fg} on {bg} measures {ratio:F2}:1, below the required {minimum}:1.");
        }
    }

    // The reason screen.danger exists at all. If warn red ever passed on its own fill,
    // the new brand value would be unnecessary and this unit's premise would be wrong.
    [Fact]
    public void The_document_warn_red_still_fails_on_its_own_fill_which_is_why_danger_exists()
    {
        var document = SemanticTestSupport.Config().Style.Colors;

        Assert.True(Contrast.Ratio(document["warnRedBorder"], document["warnRedFill"]) < TextMinimum);
    }
}
