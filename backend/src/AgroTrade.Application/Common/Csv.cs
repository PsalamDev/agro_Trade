using System.Text;

namespace AgroTrade.Application.Common;

/// <summary>
/// A minimal RFC 4180 CSV writer, for the operator exports.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than pulled from a package, because the only requirement is correct quoting.
/// The exports have fixed columns and a known shape; a library that also parses, maps objects and
/// infers types is far more surface than is needed, and it would put a dependency between the
/// export and the operator's ability to read their own data.
/// </para>
/// <para>
/// The one thing it must get right is that a comma, a quote or a line break <i>inside</i> a field
/// does not shift a column. That is not cosmetic: the reason on a rejected verification is free
/// text, and a stray comma in it would otherwise turn one column into two and silently misalign
/// every value after it. Such a field is quoted, and any embedded quote is doubled.
/// </para>
/// </remarks>
public static class Csv
{
    /// <summary>
    /// CRLF, as RFC 4180 specifies. Written explicitly rather than via
    /// <see cref="TextWriter.WriteLine(string?)"/> so the same bytes are produced on any host: the
    /// file is opened in a spreadsheet, and Excel is the least forgiving reader of the two line
    /// endings.
    /// </summary>
    public const string NewLine = "\r\n";

    /// <summary>Quotes a field only when it must be, doubling any embedded quote.</summary>
    public static string Encode(string? value)
    {
        var text = value ?? string.Empty;

        // AsSpan().IndexOfAny(string) uses the ReadOnlySpan<char> overload, so the search does not
        // allocate a char[] per field.
        if (text.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            return text;
        }

        return string.Concat("\"", text.Replace("\"", "\"\""), "\"");
    }

    /// <summary>Encodes one row, comma-separated and CRLF-terminated.</summary>
    public static string Row(params string?[] fields)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Encode(fields[i]));
        }

        return builder.Append(NewLine).ToString();
    }
}
