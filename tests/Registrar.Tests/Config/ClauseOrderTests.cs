using System.Linq;
using Registrar.Core.Config;
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Config;

public class ClauseOrderTests
{
    [Fact]
    public void ConfigBundle_exposes_clauses_in_library_order()
    {
        var config = SemanticTestSupport.Config();
        // Pin the exact clause order from clause-library.json. This is the order the SCL
        // aggregation renders; a re-sort or dropped/added clause fails here with a clear diff
        // rather than passing on a coincidental sorted-equals-file-order tautology.
        var ids = config.Clauses.Select(c => c.ClauseId).ToList();
        Assert.Equal(
            new[] { "CL-001", "CL-002", "CL-003", "CL-004", "CL-005", "CL-006", "CL-007", "CL-008", "CL-009", "CL-010" },
            ids);
    }
}
