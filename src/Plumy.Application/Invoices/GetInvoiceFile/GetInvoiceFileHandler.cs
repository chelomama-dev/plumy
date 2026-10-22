using Plumy.Application.Abstractions;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.GetInvoiceFile;

/// <summary>The uploaded file, ready to stream back. The caller disposes <see cref="Content"/>.</summary>
public sealed record InvoiceFile(Stream Content, string ContentType, string FileName);

public sealed class GetInvoiceFileHandler(IInvoiceRepository repository, IFileStorage storage)
{
    /// <returns>Null if the invoice doesn't exist for this firm.</returns>
    public async Task<InvoiceFile?> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The row is looked up first, through the tenant filter: the storage path alone is never trusted
        var invoice = await repository.GetByIdAsync(id, cancellationToken);
        if (invoice is null)
            return null;

        var content = await storage.OpenReadAsync(invoice.StoragePath, cancellationToken)
            ?? throw new InvalidOperationException($"The file of invoice {invoice.Id} is missing from storage.");

        return new InvoiceFile(content, invoice.Format.ContentType(), invoice.FileName);
    }
}
