using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.Export;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Common;
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

    public async Task<IReadOnlyList<InvoiceExportSource>> ListForExportAsync(
        Guid? clientCompanyId, DateOnly? from, DateOnly? to, int max, CancellationToken cancellationToken = default)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.ExtractionStatus == InvoiceExtractionStatus.Extracted);

        if (clientCompanyId is { } companyId)
            query = query.Where(i => i.ClientCompanyId == companyId);
        if (from is { } start)
            query = query.Where(i => i.ExtractedData!.IssueDate >= start);
        if (to is { } end)
            query = query.Where(i => i.ExtractedData!.IssueDate <= end);

        // Invoices with no date last; the id keeps the order stable
        var rows = await query
            .OrderBy(i => i.ExtractedData!.IssueDate == null)
            .ThenBy(i => i.ExtractedData!.IssueDate)
            .ThenBy(i => i.ExtractedData!.InvoiceNumber)
            .ThenBy(i => i.Id)
            .Take(max)
            .Join(
                db.ClientCompanies.IgnoreQueryFilters([QueryFilters.NotArchived]),
                i => i.ClientCompanyId,
                c => c.Id,
                (i, c) => new { Invoice = i, c.Name, c.TaxId })
            .ToListAsync(cancellationToken);

        // The join keeps the order in practice, but SQL doesn't promise it
        return rows
            .Select(r => new InvoiceExportSource(r.Invoice, r.Name, r.TaxId.Value))
            .OrderBy(r => r.Invoice.ExtractedData?.IssueDate is null)
            .ThenBy(r => r.Invoice.ExtractedData?.IssueDate)
            .ThenBy(r => r.Invoice.ExtractedData?.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Invoice.Id)
            .ToList();
    }

    public async Task<TaxId?> GetClientCompanyTaxIdAsync(
        Guid clientCompanyId, CancellationToken cancellationToken = default) =>
        await db.ClientCompanies.IgnoreQueryFilters([QueryFilters.NotArchived])
            .Where(c => c.Id == clientCompanyId)
            .Select(c => c.TaxId)
            .SingleOrDefaultAsync(cancellationToken);

    // Two copies read at the same moment can miss each other; rare, and the reviewer still sees both
    public async Task<Guid?> FindDuplicateAsync(
        Guid invoiceId, Guid clientCompanyId, string supplierTaxId, string invoiceNumber,
        CancellationToken cancellationToken = default) =>
        await db.Invoices.AsNoTracking()
            .Where(i => i.ClientCompanyId == clientCompanyId
                && i.Id != invoiceId
                && i.ExtractionStatus == InvoiceExtractionStatus.Extracted
                && i.ExtractedData!.SupplierTaxId == supplierTaxId
                && i.ExtractedData.InvoiceNumber == invoiceNumber)
            .OrderBy(i => i.UploadedAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
