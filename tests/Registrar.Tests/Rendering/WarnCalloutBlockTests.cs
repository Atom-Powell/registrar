using System.Linq;
using Registrar.Rendering;
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class WarnCalloutBlockTests
{
    private static StyleRules Style() => SemanticTestSupport.Config().Style;
    private static WarnCalloutStyle Warn() => SemanticTestSupport.Config().WarnCallout;

    [Fact]
    public void Renders_a_bordered_box_with_the_label_and_text()
    {
        var table = WarnCalloutBlock.Render("Handle with care.", Warn(), Style());

        var text = table.InnerText;
        Assert.Contains("Warning", text);
        Assert.Contains("Handle with care.", text);

        // The box uses the warn-red border color from config, not a default.
        var expectedBorder = OpenXmlHelpers.Hex(Style().Colors["warnRedBorder"]);
        var top = table.Descendants<TopBorder>().Single();
        Assert.Equal(expectedBorder, top.Color!.Value);

        // The cell is shaded with the warn-red fill.
        var expectedFill = OpenXmlHelpers.Hex(Style().Colors["warnRedFill"]);
        var shading = table.Descendants<Shading>().Single();
        Assert.Equal(expectedFill, shading.Fill!.Value);
    }
}
