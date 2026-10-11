using AgroTrade.Application.Common;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The CSV writer behind the operator exports.
/// </summary>
/// <remarks>
/// The escaping is the whole contract: an audit rejection reason is free text, and a comma inside it
/// must not be allowed to shift every column after it. These tests pin the two behaviours a reader
/// depends on — a field is quoted only when it has to be, and an embedded quote is doubled.
/// </remarks>
public sealed class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("with space", "with space")]
    [InlineData("a;b", "a;b")]
    public void Encode_LeavesFieldsWithoutDelimitersUnquoted(string? input, string expected)
    {
        Assert.Equal(expected, Csv.Encode(input));
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("line one\r\nline two", "\"line one\r\nline two\"")]
    [InlineData("line one\nline two", "\"line one\nline two\"")]
    public void Encode_QuotesFieldsContainingDelimiters(string input, string expected)
    {
        Assert.Equal(expected, Csv.Encode(input));
    }

    [Fact]
    public void Encode_DoublesEmbeddedQuotes()
    {
        // The outer pair wraps the field; the inner doubled pair is the original quote, escaped.
        Assert.Equal("\"He said \"\"no\"\"\"", Csv.Encode("He said \"no\""));
    }

    [Fact]
    public void Row_SeparatesFieldsAndTerminatesWithCrlf()
    {
        Assert.Equal("a,b,c\r\n", Csv.Row("a", "b", "c"));
        Assert.Equal("a,\"b,c\"\r\n", Csv.Row("a", "b,c"));
    }

    [Fact]
    public void Row_KeepsAnEmbeddedCommaInsideOneColumn()
    {
        // If the comma were not escaped this would read back as three columns, and every later value
        // in the row would be attributed to the wrong heading.
        Assert.Equal(
            "REJECTED,\"Certificate of origin, illegible.\"\r\n",
            Csv.Row("REJECTED", "Certificate of origin, illegible."));
    }
}
