using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.ClientCompanies;

public interface IClientCompanyRepository
{
    Task<ClientCompany?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByTaxIdAsync(TaxId taxId, CancellationToken cancellationToken = default);

    // Only tracks the entity: IUnitOfWork.SaveChangesAsync persists it
    void Add(ClientCompany company);
}
