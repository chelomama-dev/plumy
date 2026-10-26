using Plumy.Application.Common;

namespace Plumy.Application.Invoices.ListInvoices;

public sealed class ListInvoicesHandler(IInvoiceRepository repository)
{
    public Task<PagedResult<InvoiceResponse>> HandleAsync(
        ListInvoicesQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query.ClientCompanyId, query.ReviewStatus, query.Page, query.PageSize, cancellationToken);
}
