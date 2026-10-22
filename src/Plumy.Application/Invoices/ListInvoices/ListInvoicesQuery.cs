namespace Plumy.Application.Invoices.ListInvoices;

public sealed record ListInvoicesQuery(int Page, int PageSize, Guid? ClientCompanyId);
