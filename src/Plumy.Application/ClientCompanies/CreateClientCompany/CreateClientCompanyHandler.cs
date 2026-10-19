using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.ClientCompanies.CreateClientCompany;

public sealed class CreateClientCompanyHandler(
    IClientCompanyRepository repository, IUnitOfWork unitOfWork, ITenantContext tenant)
{
    public async Task<ClientCompanyResponse> HandleAsync(
        CreateClientCompanyCommand command, CancellationToken cancellationToken = default)
    {
        // Creating the TaxId first normalizes it, so " b-1234567.4 " and "B12345674" are the same company
        var taxId = TaxId.Create(command.TaxId);

        // Tax IDs are unique per firm: two firms can keep the books of the same company.
        // Fast path with a friendly message; the unique index is still the real guarantee under concurrency
        var existing = await repository.GetByTaxIdIncludingArchivedAsync(taxId, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException(existing.IsArchived
                ? $"A client company with tax ID '{taxId}' already exists but is archived."
                : $"A client company with tax ID '{taxId}' already exists.");
        }

        var company = ClientCompany.Create(tenant.FirmId, command.Name, taxId);

        repository.Add(company);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ClientCompanyResponse.From(company);
    }
}
