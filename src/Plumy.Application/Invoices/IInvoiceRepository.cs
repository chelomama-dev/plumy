using Plumy.Application.Common;
using Plumy.Application.Invoices.Export;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One invoice with its company's name, as the API returns it.</summary>
    Task<InvoiceResponse?> GetResponseByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages invoices, newest first, optionally of one client company. Invoices of archived companies stay
    /// visible: they are still part of the books.
    /// </summary>
    Task<PagedResult<InvoiceResponse>> ListAsync(
        Guid? clientCompanyId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid clientCompanyId, string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read invoices (status Extracted) with their company, by issue date then number, at most <paramref name="max"/>.
    /// A date range leaves out invoices with no date.
    /// </summary>
    Task<IReadOnlyList<InvoiceExportSource>> ListForExportAsync(
        Guid? clientCompanyId, DateOnly? from, DateOnly? to, int max, CancellationToken cancellationToken = default);

    /// <summary>The NIF of the invoice's company, archived or not: what the invoice should be addressed to.</summary>
    Task<TaxId?> GetClientCompanyTaxIdAsync(Guid clientCompanyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The earliest other invoice of the company already read with the same supplier NIF and invoice number, if any.
    /// </summary>
    Task<Guid?> FindDuplicateAsync(
        Guid invoiceId, Guid clientCompanyId, string supplierTaxId, string invoiceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>The account a person confirmed most recently for this supplier (by NIF) in this company.</summary>
    Task<string?> FindConfirmedLedgerAccountAsync(
        Guid clientCompanyId, string supplierTaxId, CancellationToken cancellationToken = default);

    /// <summary>The company's most recently confirmed accounts, newest first: the classifier's examples.</summary>
    Task<IReadOnlyList<ConfirmedLedgerAccountExample>> ListConfirmedLedgerAccountsAsync(
        Guid clientCompanyId, int max, CancellationToken cancellationToken = default);

    // Only tracks the entity: IUnitOfWork.SaveChangesAsync persists it
    void Add(Invoice invoice);
}
