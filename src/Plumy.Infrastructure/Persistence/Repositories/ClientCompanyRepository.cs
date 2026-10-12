using Microsoft.EntityFrameworkCore;
using Plumy.Application.ClientCompanies;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Infrastructure.Persistence.Repositories;

internal sealed class ClientCompanyRepository(PlumyDbContext db) : IClientCompanyRepository
{
    // FindAsync checks the change tracker before hitting the database
    public async Task<ClientCompany?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ClientCompanies.FindAsync([id], cancellationToken);

    // The value converter turns the TaxId into its string, so this is a plain index seek in SQL
    public Task<bool> ExistsByTaxIdAsync(TaxId taxId, CancellationToken cancellationToken = default) =>
        db.ClientCompanies.AnyAsync(c => c.TaxId == taxId, cancellationToken);

    public void Add(ClientCompany company) => db.ClientCompanies.Add(company);
}
