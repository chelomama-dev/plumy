using Plumy.Application.Invoices.Export;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Tests.Export;

internal static class ExportRows
{
    public static readonly Guid InvoiceId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    public static InvoiceExportRow Row(
        string? supplierName = "Endesa Energía, S.A.U.", decimal? total = 1493.82m,
        IReadOnlyList<InvoiceIssue>? issues = null) =>
        new("Acme S.L.", "B12345674", new DateOnly(2026, 10, 1), "F-42", supplierName, "A81948077", 1234.56m,
            259.26m, total, "EUR", "628", "Suministros",
            issues ?? [], "luz.pdf", InvoiceId);
}
