namespace Plumy.Application.Invoices.GetInvoiceById;

public sealed class GetInvoiceByIdHandler(IInvoiceRepository repository)
{
    public Task<InvoiceResponse?> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetResponseByIdAsync(id, cancellationToken);
}
