using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.ClientCompanies.CreateClientCompany;

public sealed class CreateClientCompanyHandler(IClientCompanyRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<ClientCompanyResponse> HandleAsync(
        CreateClientCompanyCommand command, CancellationToken cancellationToken = default)
    {
        // Creating the TaxId first normalizes it, so " b-1234567.4 " and "B12345674" are the same company
        var taxId = TaxId.Create(command.TaxId);

        // Fast path with a friendly message; the unique index is still the real guarantee under concurrency
        if (await repository.ExistsByTaxIdAsync(taxId, cancellationToken))
            throw new ConflictException($"A client company with tax ID '{taxId}' already exists.");

        var company = ClientCompany.Create(command.Name, taxId);

        repository.Add(company);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ClientCompanyResponse.From(company);
    }
}
