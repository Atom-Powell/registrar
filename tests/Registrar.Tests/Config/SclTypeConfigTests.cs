using Registrar.Core.Config;
using Xunit;

namespace Registrar.Tests.Config;

public class SclTypeConfigTests
{
    [Fact]
    public void SCL_has_a_defined_section_schema()
    {
        var config = SemanticTestSupport.Config();
        var scl = config.ArtifactTypesByCode["SCL"];
        Assert.NotNull(scl.SectionSchema);
        Assert.Equal("defined", scl.SectionSchema!.Status);
        Assert.Equal(
            new[] { "Purpose", "How Clauses Are Inserted", "Clause Library", "Version and Change Notes" },
            scl.SectionSchema.OrderedSections);
    }
}
