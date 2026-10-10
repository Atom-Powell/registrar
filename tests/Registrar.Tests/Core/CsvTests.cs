using Registrar.Core;

namespace Registrar.Tests.Core;

public class CsvTests
{
    [Fact]
    public void Parse_reads_simple_rows()
    {
        var rows = Csv.Parse("a,b\n1,2\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_handles_quoted_field_with_comma()
    {
        var rows = Csv.Parse("id,client\nX,\"Talewind CPA, LLC\"\n");
        Assert.Equal("Talewind CPA, LLC", rows[1][1]);
    }

    [Fact]
    public void Parse_handles_doubled_quotes_and_crlf()
    {
        var rows = Csv.Parse("a\r\n\"he said \"\"hi\"\"\"\r\n");
        Assert.Equal("a", rows[0][0]);
        Assert.Equal("he said \"hi\"", rows[1][0]);
    }

    [Fact]
    public void Parse_final_row_without_trailing_newline()
    {
        var rows = Csv.Parse("a,b\n1,2");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }
}
