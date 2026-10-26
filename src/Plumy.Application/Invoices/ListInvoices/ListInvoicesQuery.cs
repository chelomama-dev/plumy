using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ListInvoices;

/// <param name="ReviewStatus">Only invoices in this review status; null, any.</param>
public sealed record ListInvoicesQuery(
    int Page, int PageSize, Guid? ClientCompanyId, InvoiceReviewStatus? ReviewStatus = null);
