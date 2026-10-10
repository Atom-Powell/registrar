using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class QaCatalogTests
{
    private const string Json = """
    { "checks": [
        { "checkId": "QA-X-01", "ruleSource": "DOC 1.1", "errorCategory": "MetadataViolation" },
        { "checkId": "QA-X-02", "ruleSource": "DOC 2.2" }
    ] }
    """;

    private static Check Sample(string id) => new()
    {
        CheckId = id,
        LayerName = "content",
        Result = "fail",
        SeverityLevel = "blocking",
        Message = "m",
        RuleSource = "CODE-OLD",
        ErrorCategory = "CodeOld"
    };

    [Fact]
    public void Stamp_overrides_ruleSource_and_errorCategory_from_the_catalog()
    {
        var stamped = QaCatalog.Parse(Json).Stamp(Sample("QA-X-01"));
        Assert.Equal("DOC 1.1", stamped.RuleSource);
        Assert.Equal("MetadataViolation", stamped.ErrorCategory);
    }

    [Fact]
    public void Stamp_keeps_code_errorCategory_when_the_catalog_omits_it()
    {
        // QA-X-02 declares no errorCategory; a multi-category check keeps its code value.
        var stamped = QaCatalog.Parse(Json).Stamp(Sample("QA-X-02"));
        Assert.Equal("DOC 2.2", stamped.RuleSource);
        Assert.Equal("CodeOld", stamped.ErrorCategory);
    }

    [Fact]
    public void Stamp_keeps_errorCategory_null_for_a_passing_check()
    {
        // Code signals "no violation" with a null errorCategory; the catalog category
        // must not be stamped onto a passing check.
        var passing = Sample("QA-X-01") with { Result = "pass", ErrorCategory = null };
        var stamped = QaCatalog.Parse(Json).Stamp(passing);
        Assert.Null(stamped.ErrorCategory);
    }

    [Fact]
    public void Stamp_preserves_every_other_field()
    {
        var stamped = QaCatalog.Parse(Json).Stamp(Sample("QA-X-01"));
        Assert.Equal("m", stamped.Message);
        Assert.Equal("fail", stamped.Result);
        Assert.Equal("QA-X-01", stamped.CheckId);
    }

    [Fact]
    public void Stamp_throws_when_the_checkId_is_absent_from_the_catalog()
    {
        var cat = QaCatalog.Parse(Json);
        Assert.Throws<InvalidOperationException>(() => cat.Stamp(Sample("QA-UNKNOWN")));
    }
}
