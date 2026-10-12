using Microsoft.EntityFrameworkCore;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Infrastructure.Persistence.Repositories;

internal sealed class ClientCompanyRepository(PlumyDbContext db) : IClientCompanyRepository
{
    // FindAsync checks the change tracker before hitting the database
    public async Task<ClientCompany?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ClientCompanies.FindAsync([id], cancellationToken);

    public async Task<PagedResult<ClientCompany>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Read-only: no need for the change tracker to keep snapshots of every row
        var query = db.ClientCompanies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // A valid tax ID is an exact match that uses the unique index; anything else searches the name.
            // Case-insensitive thanks to SQL Server's default collation (CI_AS)
            query = TaxId.TryCreate(term, out var taxId)
                ? query.Where(c => c.TaxId == taxId)
                : query.Where(c => c.Name.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // long avoids overflow with huge page numbers; past the end there is nothing to fetch
        var skip = (long)(page - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<ClientCompany>([], page, pageSize, totalCount);

        // Id breaks ties between equal names: without a unique order, rows can repeat or vanish across pages
        var items = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ClientCompany>(items, page, pageSize, totalCount);
    }

    // The value converter turns the TaxId into its string, so this is a plain index seek in SQL
    public Task<bool> ExistsByTaxIdAsync(TaxId taxId, CancellationToken cancellationToken = default) =>
        db.ClientCompanies.AnyAsync(c => c.TaxId == taxId, cancellationToken);

    public void Add(ClientCompany company) => db.ClientCompanies.Add(company);
}
