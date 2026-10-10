using Registrar.Core.Model;

namespace Registrar.Tests.Model;

public class AgrContextTests
{
    [Fact]
    public void Parse_maps_context_and_rendering_metadata()
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");

        Assert.Equal("Registry Owner", agr.Context.Owner);
        Assert.Equal("Talewind CPA", agr.Context.Client);
        Assert.Equal("ORG Consulting, LLC", agr.Context.PreparedBy);
        Assert.False(string.IsNullOrEmpty(agr.Context.Audience));
        Assert.Equal("Governed AI Rollout", agr.RenderingOptions.PackageName);
        Assert.EndsWith(".docx", agr.RenderingOptions.OutputFileName);
    }
}
