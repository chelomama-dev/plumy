using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.ClientCompanies;

public interface IClientCompanyRepository
{
    Task<ClientCompany?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages client companies ordered by name. <paramref name="search"/> matches a tax ID exactly when it is
    /// a valid one, otherwise any part of the name.
    /// </summary>
    Task<PagedResult<ClientCompany>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<bool> ExistsByTaxIdAsync(TaxId taxId, CancellationToken cancellationToken = default);

    // Only tracks the entity: IUnitOfWork.SaveChangesAsync persists it
    void Add(ClientCompany company);
}
