using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Persistence.Repositories;

internal sealed class InvoiceRepository(PlumyDbContext db) : IInvoiceRepository
{
    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Invoices.FindAsync([id], cancellationToken);

    public async Task<InvoiceResponse?> GetResponseByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await WithCompanyName(db.Invoices.AsNoTracking().Where(i => i.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : InvoiceResponse.From(row.Invoice, row.CompanyName);
    }

    public async Task<PagedResult<InvoiceResponse>> ListAsync(
        Guid? clientCompanyId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Invoices.AsNoTracking();

        if (clientCompanyId is { } companyId)
            query = query.Where(i => i.ClientCompanyId == companyId);

        var totalCount = await query.CountAsync(cancellationToken);

        var skip = (long)(page - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<InvoiceResponse>([], page, pageSize, totalCount);

        // Ids are GUID v7, so they grow with time too: a unique tie-breaker for invoices uploaded in the same instant
        var paged = query
            .OrderByDescending(i => i.UploadedAt)
            .ThenByDescending(i => i.Id)
            .Skip((int)skip)
            .Take(pageSize);

        var rows = await WithCompanyName(paged).ToListAsync(cancellationToken);

        // The join keeps the page's order in practice, but SQL doesn't promise it: order again in memory
        var items = rows
            .OrderByDescending(r => r.Invoice.UploadedAt)
            .ThenByDescending(r => r.Invoice.Id)
            .Select(r => InvoiceResponse.From(r.Invoice, r.CompanyName))
            .ToList();

        return new PagedResult<InvoiceResponse>(items, page, pageSize, totalCount);
    }

    public Task<bool> ExistsAsync(Guid clientCompanyId, string sha256, CancellationToken cancellationToken = default) =>
        db.Invoices.AnyAsync(i => i.ClientCompanyId == clientCompanyId && i.Sha256 == sha256, cancellationToken);

    public Task<string?> FindConfirmedLedgerAccountAsync(
        Guid clientCompanyId, string supplierTaxId, CancellationToken cancellationToken = default) =>
        db.Invoices.AsNoTracking()
            .Where(i => i.ClientCompanyId == clientCompanyId
                && i.LedgerAccountCode != null
                && i.ExtractedData!.SupplierTaxId == supplierTaxId)
            .OrderByDescending(i => i.LedgerAccountConfirmedAt)
            .Select(i => i.LedgerAccountCode)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConfirmedLedgerAccountExample>> ListConfirmedLedgerAccountsAsync(
        Guid clientCompanyId, int max, CancellationToken cancellationToken = default) =>
        await db.Invoices.AsNoTracking()
            .Where(i => i.ClientCompanyId == clientCompanyId && i.LedgerAccountCode != null)
            .OrderByDescending(i => i.LedgerAccountConfirmedAt)
            .Take(max)
            .Select(i => new ConfirmedLedgerAccountExample(
                i.ExtractedData!.SupplierName, i.ExtractedData.ItemsDescription, i.LedgerAccountCode!))
            .ToListAsync(cancellationToken);

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    // Archived companies still name their invoices; the firm filter stays on
    private IQueryable<InvoiceRow> WithCompanyName(IQueryable<Invoice> invoices) =>
        invoices.Join(
            db.ClientCompanies.IgnoreQueryFilters([QueryFilters.NotArchived]),
            i => i.ClientCompanyId,
            c => c.Id,
            (i, c) => new InvoiceRow(i, c.Name));

    private sealed record InvoiceRow(Invoice Invoice, string CompanyName);
}
