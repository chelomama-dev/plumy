using System.Text;
using Plumy.Application.Invoices.Export;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Export;
using static Plumy.Infrastructure.Tests.Export.ExportRows;

namespace Plumy.Infrastructure.Tests.Export;

public sealed class CsvInvoiceExportWriterTests
{
    private const string Header =
        "Empresa;NIF empresa;Fecha;Número;Proveedor;NIF proveedor;Base imponible;IVA;Total;Moneda;Cuenta;" +
        "Nombre de la cuenta;Incidencias;Archivo;Id";

    private static async Task<byte[]> WriteAsync(params InvoiceExportRow[] rows)
    {
        using var output = new MemoryStream();
        await new CsvInvoiceExportWriter().WriteAsync(rows, output);
        return output.ToArray();
    }

    private static async Task<string[]> LinesAsync(params InvoiceExportRow[] rows) =>
        Encoding.UTF8.GetString(await WriteAsync(rows))[1..].Split("\r\n");

    [Fact]
    public async Task Starts_with_a_utf8_bom_so_excel_reads_the_accents()
    {
        var bytes = await WriteAsync();

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public async Task Writes_the_header_and_a_line_per_invoice_the_spanish_way()
    {
        var lines = await LinesAsync(Row());

        Assert.Equal(
        [
            Header,
            // Semicolons, decimal comma without thousands, dd/MM/yyyy; the name with a comma needs no quotes
            "Acme S.L.;B12345674;01/10/2026;F-42;Endesa Energía, S.A.U.;A81948077;1234,56;259,26;1493,82;EUR;628;" +
            $"Suministros;;luz.pdf;{InvoiceId}",
            ""
        ], lines);
    }

    [Fact]
    public async Task Missing_values_are_empty_and_issues_are_listed()
    {
        var lines = await LinesAsync(Row(total: null, issues: [InvoiceIssue.MissingTotal, InvoiceIssue.PossibleDuplicate]));

        var cells = lines[1].Split(';');
        Assert.Equal("", cells[8]);
        Assert.Equal("Falta el total | Posible duplicada", cells[12]);
    }

    [Fact]
    public async Task A_negative_amount_stays_a_number()
    {
        var lines = await LinesAsync(Row(total: -121m));

        Assert.Equal("-121,00", lines[1].Split(';')[8]);
    }

    [Theory]
    [InlineData("Papelería \"El Sol\"", "\"Papelería \"\"El Sol\"\"\"")]
    [InlineData("Uno; Dos", "\"Uno; Dos\"")]
    [InlineData("Línea\nDos", "\"Línea\nDos\"")]
    public void Quotes_what_would_break_the_line(string text, string expected)
    {
        Assert.Equal(expected, CsvInvoiceExportWriter.Escape(text));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("+34 600 000 000")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public async Task Text_that_excel_would_run_as_a_formula_is_neutralized(string supplierName)
    {
        var lines = await LinesAsync(Row(supplierName: supplierName));

        Assert.Contains("'" + supplierName.Replace("\"", "\"\""), lines[1]);
    }

    [Fact]
    public void Says_what_it_is()
    {
        var writer = new CsvInvoiceExportWriter();

        Assert.Equal((InvoiceExportFormat.Csv, "text/csv; charset=utf-8", ".csv"),
            (writer.Format, writer.ContentType, writer.Extension));
    }
}
