using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices;

public sealed record InvoiceResponse(
    Guid Id,
    Guid ClientCompanyId,
    string ClientCompanyName,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt)
{
    public static InvoiceResponse From(Invoice invoice, string clientCompanyName) =>
        new(invoice.Id, invoice.ClientCompanyId, clientCompanyName, invoice.FileName, invoice.Format.ContentType(),
            invoice.SizeBytes, invoice.UploadedAt);
}
