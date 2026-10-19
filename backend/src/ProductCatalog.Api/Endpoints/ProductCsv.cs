using System.Globalization;
using System.Text;
using ProductCatalog.Application.Products;

namespace ProductCatalog.Api.Endpoints;

/// <summary>
/// Writes products as CSV: RFC 4180 (comma-separated, CRLF line ends, quotes doubled), with a UTF-8 byte order mark
/// so Excel recognises the encoding. Rows are written as they are read, so the export never sits in memory as a whole.
/// </summary>
internal static class ProductCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    private const string Header = "Id,Name,SKU,Category,Price,Stock,Description,UpdatedAt";

    public static async Task WriteAsync(Stream body, IAsyncEnumerable<ProductDto> products, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(Header);
        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(string.Join(
                ',',
                product.Id.ToString(CultureInfo.InvariantCulture),
                Text(product.Name),
                Text(product.Sku),
                Text(product.CategoryName),
                product.Price.ToString("0.00", CultureInfo.InvariantCulture),
                product.Stock.ToString(CultureInfo.InvariantCulture),
                Text(product.Description),
                product.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// A text cell, quoted when it holds a comma, quote or line break. A spreadsheet runs a cell that starts with
    /// =, +, -, @, tab or carriage return as a formula, so such a value (e.g. a product named "=HYPERLINK(...)")
    /// gets a leading apostrophe and stays plain text (OWASP's advice against CSV injection).
    /// </summary>
    internal static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
