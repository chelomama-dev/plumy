using System.Globalization;
using System.Text;
using Plumy.Application.Invoices.Export;
using static Plumy.Infrastructure.Export.InvoiceExportColumns;

namespace Plumy.Infrastructure.Export;

/// <summary>
/// CSV as Excel in Spanish expects it, so a double click opens it right: semicolons (the comma is the decimal
/// separator), decimal comma, dd/MM/yyyy dates, CRLF and UTF-8 with a BOM (without it, Excel reads the accents as
/// Windows-1252). Written by hand: a few lines, and every one of those choices is explicit.
/// </summary>
internal sealed class CsvInvoiceExportWriter : IInvoiceExportWriter
{
    private const char Separator = ';';
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    // The first characters that make Excel (and others) read a cell as a formula: =HYPERLINK(...) in a supplier's
    // name would run when the accountant opens the file (CSV injection, OWASP)
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public InvoiceExportFormat Format => InvoiceExportFormat.Csv;
    public string ContentType => "text/csv; charset=utf-8";
    public string Extension => ".csv";

    public async Task WriteAsync(
        IReadOnlyList<InvoiceExportRow> rows, Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            leaveOpen: true) { NewLine = "\r\n" };

        await writer.WriteLineAsync(string.Join(Separator, All.Select(c => Escape(c.Header))));

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(Separator, All.Select(c => FormatCell(c, row))));
        }
    }

    private static string FormatCell(Column column, InvoiceExportRow row) => column.Value(row) switch
    {
        null => "",
        // Amounts are written by us, never escaped: -121,00 is a credit note, not a formula
        decimal amount => amount.ToString("0.00", Spanish),
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        var value => Escape(NeutralizeFormula(value.ToString() ?? ""))
    };

    // A leading apostrophe makes Excel show the text as it is
    internal static string NeutralizeFormula(string text) =>
        text.Length > 0 && FormulaStarts.Contains(text[0]) ? "'" + text : text;

    // RFC 4180: quotes around anything with a separator, a quote or a line break; quotes inside doubled
    internal static string Escape(string text) =>
        text.IndexOfAny([Separator, '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
