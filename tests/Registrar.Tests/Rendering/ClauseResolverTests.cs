using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Rendering;

namespace Registrar.Tests.Rendering;

public class ClauseResolverTests
{
    private static IReadOnlyDictionary<string, ClauseEntry> Lib(ClauseEntry e)
        => new Dictionary<string, ClauseEntry> { [e.ClauseId] = e };

    private static ClauseEntry Cl(string id, string text)
        => new() { ClauseId = id, CanonicalText = text, CanonicalTextSha256 = Hashing.Sha256Hex(text) };

    [Fact]
    public void Resolves_tokens_and_verifies_sha()
    {
        var lib = Lib(Cl("CL-X", "Use [TOOL] only."));
        var map = new Dictionary<string, string?> { ["[TOOL]"] = "Copilot" };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, lib, map);
        Assert.Null(r.Failure);
        Assert.Equal("Use Copilot only.", r.Text);
        Assert.True(r.Audit!.Verified);
        Assert.Equal(1, r.Audit.TokensResolved);
    }

    [Fact]
    public void Leaves_unmapped_token_as_literal()
    {
        var lib = Lib(Cl("CL-X", "Use [TOOL] only."));
        var map = new Dictionary<string, string?> { ["[TOOL]"] = null };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, lib, map);
        Assert.Equal("Use [TOOL] only.", r.Text);
        Assert.Equal(0, r.Audit!.TokensResolved);
    }

    [Fact]
    public void Null_clause_id_is_a_blocking_failure()
    {
        var r = ClauseResolver.Resolve(null, 1, 2, Lib(Cl("CL-X", "x")), new Dictionary<string, string?>());
        Assert.Null(r.Text);
        Assert.NotNull(r.Failure);
        Assert.Equal(CheckResultValue.Fail, r.Failure!.Result);
        Assert.Equal(Severity.Blocking, r.Failure.SeverityLevel);
        Assert.Equal("/dataInputs/sections/1/blocks/2/clauseId", r.Failure.JsonPath);
    }

    [Fact]
    public void Sha_mismatch_is_a_blocking_failure()
    {
        var tampered = new ClauseEntry { ClauseId = "CL-X", CanonicalText = "real text",
            CanonicalTextSha256 = new string('0', 64) };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, Lib(tampered), new Dictionary<string, string?>());
        Assert.Null(r.Text);
        Assert.NotNull(r.Failure);
        Assert.False(r.Audit!.Verified);
    }
}
