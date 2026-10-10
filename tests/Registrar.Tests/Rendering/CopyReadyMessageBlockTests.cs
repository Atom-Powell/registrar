using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Rendering;
using Xunit;

namespace Registrar.Tests.Rendering;

public class CopyReadyMessageBlockTests
{
    [Fact]
    public void Renders_a_box_with_label_and_message_text()
    {
        var config = SemanticTestSupport.Config();
        var block = new Registrar.Core.Model.AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Attached is your governed delivery package. Please review and confirm."
        };
        var result = BlockRenderer.Render(block, 0, 0,
            new RenderContext(config.Style, config.ClausesById, new Dictionary<string, string?>(),
                config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage,
                config.CanonicalTokens));

        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        Assert.Contains(config.CopyReadyMessage.Label, table.InnerText);
        Assert.Contains("Attached is your governed delivery package", table.InnerText);
        Assert.Equal(block.Text, result.ScanText);
    }
}
