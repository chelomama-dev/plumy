using ClosedXML.Excel;
using Plumy.Application.Invoices.Export;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Export;
using static Plumy.Infrastructure.Tests.Export.ExportRows;

namespace Plumy.Infrastructure.Tests.Export;

// Written, then opened again with ClosedXML: what Excel would find in the file
public sealed class XlsxInvoiceExportWriterTests
{
    private static async Task<XLWorkbook> WriteAsync(params InvoiceExportRow[] rows)
    {
        var output = new MemoryStream();
        await new XlsxInvoiceExportWriter().WriteAsync(rows, output);
        output.Position = 0;
        return new XLWorkbook(output);
    }

    [Fact]
    public async Task Has_a_bold_frozen_header_with_filters()
    {
        using var workbook = await WriteAsync(Row());
        var sheet = workbook.Worksheet(XlsxInvoiceExportWriter.SheetName);

        Assert.Equal(["Empresa", "NIF empresa", "Fecha", "Número", "Proveedor"],
            sheet.Row(1).Cells(1, 5).Select(c => c.GetText()));
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.True(sheet.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task Amounts_and_dates_are_real_numbers_and_dates()
    {
        using var workbook = await WriteAsync(Row());
        var row = workbook.Worksheet(1).Row(2);

        Assert.Equal(new DateTime(2026, 10, 1), row.Cell(3).GetDateTime());
        Assert.Equal(XlsxInvoiceExportWriter.DateFormat, row.Cell(3).Style.NumberFormat.Format);
        Assert.Equal(1234.56, row.Cell(7).GetDouble(), 2);
        Assert.Equal(1493.82, row.Cell(9).GetDouble(), 2);
        Assert.Equal(XlsxInvoiceExportWriter.AmountFormat, row.Cell(9).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Text_is_kept_as_text()
    {
        using var workbook = await WriteAsync(
            Row(supplierName: "=HYPERLINK(\"http://evil\")", issues: [InvoiceIssue.AmountsMismatch]));
        var row = workbook.Worksheet(1).Row(2);

        // Text, not a formula: it shows as typed and runs nothing
        Assert.False(row.Cell(5).HasFormula);
        Assert.Equal("=HYPERLINK(\"http://evil\")", row.Cell(5).GetText());
        Assert.Equal("Suministros", row.Cell(12).GetText());
        Assert.Equal("Base + IVA no da el total", row.Cell(13).GetText());
        Assert.Equal(InvoiceId.ToString(), row.Cell(15).GetText());
    }

    [Fact]
    public async Task Missing_values_are_blank()
    {
        using var workbook = await WriteAsync(Row(total: null));

        Assert.True(workbook.Worksheet(1).Cell(2, 9).Value.IsBlank);
    }

    [Fact]
    public async Task No_invoices_is_just_the_header()
    {
        using var workbook = await WriteAsync();

        Assert.Equal(1, workbook.Worksheet(1).LastRowUsed()?.RowNumber());
    }

    [Fact]
    public void Says_what_it_is()
    {
        var writer = new XlsxInvoiceExportWriter();

        Assert.Equal(
            (InvoiceExportFormat.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
            (writer.Format, writer.ContentType, writer.Extension));
    }
}
