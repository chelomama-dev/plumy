using Plumy.Application.Invoices.Export;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Export;

/// <summary>
/// The export's columns, shared by every format: the file is a Spanish document for Spanish accountants, so the
/// headers and labels are in Spanish.
/// </summary>
internal static class InvoiceExportColumns
{
    public enum Kind
    {
        Text,
        Date,
        Amount
    }

    public sealed record Column(string Header, Kind Kind, Func<InvoiceExportRow, object?> Value, double Width);

    public static IReadOnlyList<Column> All { get; } =
    [
        new("Empresa", Kind.Text, r => r.CompanyName, 28),
        new("NIF empresa", Kind.Text, r => r.CompanyTaxId, 13),
        new("Fecha", Kind.Date, r => r.IssueDate, 11),
        new("Número", Kind.Text, r => r.InvoiceNumber, 16),
        new("Proveedor", Kind.Text, r => r.SupplierName, 30),
        new("NIF proveedor", Kind.Text, r => r.SupplierTaxId, 14),
        new("Base imponible", Kind.Amount, r => r.NetAmount, 14),
        new("IVA", Kind.Amount, r => r.TaxAmount, 12),
        new("Total", Kind.Amount, r => r.TotalAmount, 14),
        new("Moneda", Kind.Text, r => r.Currency, 8),
        new("Cuenta", Kind.Text, r => r.LedgerAccountCode, 8),
        new("Nombre de la cuenta", Kind.Text, r => r.LedgerAccountName, 30),
        new("Incidencias", Kind.Text, r => IssuesLabel(r.Issues), 40),
        new("Archivo", Kind.Text, r => r.FileName, 24),
        new("Id", Kind.Text, r => r.InvoiceId.ToString(), 38)
    ];

    public static string IssuesLabel(IReadOnlyList<InvoiceIssue> issues) =>
        string.Join(" | ", issues.Select(IssueLabel));

    // Same wording as the web app's list
    public static string IssueLabel(InvoiceIssue issue) => issue switch
    {
        InvoiceIssue.MissingSupplierTaxId => "Falta el NIF del proveedor",
        InvoiceIssue.InvalidSupplierTaxId => "El NIF del proveedor no es válido",
        InvoiceIssue.CustomerTaxIdMismatch => "Va dirigida a otro NIF, no a esta empresa",
        InvoiceIssue.MissingInvoiceNumber => "Falta el número de factura",
        InvoiceIssue.MissingIssueDate => "Falta la fecha",
        InvoiceIssue.FutureIssueDate => "La fecha es posterior a hoy",
        InvoiceIssue.MissingTotal => "Falta el total",
        InvoiceIssue.AmountsMismatch => "Base + IVA no da el total",
        InvoiceIssue.UnusualTaxAmount => "El IVA no corresponde a ningún tipo español",
        InvoiceIssue.PossibleDuplicate => "Posible duplicada",
        _ => issue.ToString()
    };
}
