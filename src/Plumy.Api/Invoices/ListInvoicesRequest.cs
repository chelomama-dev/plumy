using System.ComponentModel.DataAnnotations;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Invoices;

/// <summary>Query string of GET /api/invoices. Public so the validation source generator sees it.</summary>
public sealed record ListInvoicesRequest(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, ListInvoicesRequest.MaxPageSize)] int PageSize = ListInvoicesRequest.DefaultPageSize,
    Guid? ClientCompanyId = null,
    InvoiceReviewStatus? ReviewStatus = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}
