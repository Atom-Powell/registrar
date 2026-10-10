using Registrar.Core.Authoring;

namespace Registrar.Tests.Authoring;

// AgrAuthoring.DefaultSources is the single claim of where each default came from,
// and these tests keep that claim to the three known origins.
public class DefaultSourceProvenanceTests
{
    [Fact]
    public void Every_default_names_one_of_the_three_known_origins()
    {
        var s = AgrAuthoring.DefaultSources;
        var known = new[]
        {
            AgrAuthoring.FromConfig, AgrAuthoring.FromSchemaDefault, AgrAuthoring.FromDerivation
        };

        Assert.All(
            new[]
            {
                s.Classification, s.Layer, s.Audience, s.Version,
                s.ApprovalStatus, s.PreparedBy, s.ReviewDate, s.SectionCount
            },
            source => Assert.Contains(source, known));
    }

    // The two that are not config, pinned by name. If a derivation moves, this fails and
    // the claim is corrected with it rather than quietly describing the old origin.
    [Fact]
    public void Prepared_by_comes_from_the_schema_and_review_date_is_derived()
    {
        Assert.Equal(AgrAuthoring.FromSchemaDefault, AgrAuthoring.DefaultSources.PreparedBy);
        Assert.Equal(AgrAuthoring.FromDerivation, AgrAuthoring.DefaultSources.ReviewDate);
    }
}
