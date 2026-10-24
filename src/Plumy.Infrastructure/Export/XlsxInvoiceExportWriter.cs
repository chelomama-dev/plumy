using ClosedXML.Excel;
using Plumy.Application.Invoices.Export;
using static Plumy.Infrastructure.Export.InvoiceExportColumns;

namespace Plumy.Infrastructure.Export;

/// <summary>
/// An Excel workbook with ClosedXML (MIT): amounts and dates are real numbers and dates, so the accountant can sum
/// and filter at once. Text cells are stored as text, never as formulas, so a supplier's name can't run anything.
/// </summary>
internal sealed class XlsxInvoiceExportWriter : IInvoiceExportWriter
{
    internal const string SheetName = "Facturas";
    internal const string AmountFormat = "#,##0.00";
    internal const string DateFormat = "dd/mm/yyyy";

    public InvoiceExportFormat Format => InvoiceExportFormat.Xlsx;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string Extension => ".xlsx";

    public Task WriteAsync(
        IReadOnlyList<InvoiceExportRow> rows, Stream output, CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);

        for (var c = 0; c < All.Count; c++)
        {
            var column = All[c];
            sheet.Cell(1, c + 1).Value = column.Header;
            sheet.Column(c + 1).Width = column.Width;
            if (column.Kind == Kind.Amount)
                sheet.Column(c + 1).Style.NumberFormat.Format = AmountFormat;
            else if (column.Kind == Kind.Date)
                sheet.Column(c + 1).Style.NumberFormat.Format = DateFormat;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var c = 0; c < All.Count; c++)
                sheet.Cell(r + 2, c + 1).Value = ToCellValue(All[c].Value(rows[r]));
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        // Filters on every column, ready to use
        sheet.Range(1, 1, rows.Count + 1, All.Count).SetAutoFilter();

        workbook.SaveAs(output);
        return Task.CompletedTask;
    }

    private static XLCellValue ToCellValue(object? value) => value switch
    {
        null => Blank.Value,
        decimal amount => amount,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        // A string is always text in ClosedXML (no formula, no guessing "0042" is a number)
        var other => other.ToString() ?? ""
    };
}
