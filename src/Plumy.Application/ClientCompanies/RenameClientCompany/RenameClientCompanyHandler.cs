using Plumy.Application.Abstractions;

namespace Plumy.Application.ClientCompanies.RenameClientCompany;

public sealed class RenameClientCompanyHandler(IClientCompanyRepository repository, IUnitOfWork unitOfWork)
{
    // Returns null when the company doesn't exist (or is archived, which is the same from the outside)
    public async Task<ClientCompanyResponse?> HandleAsync(
        RenameClientCompanyCommand command, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (company is null)
            return null;

        // No repository "Update" call: the entity is tracked, so SaveChanges detects the change by itself
        company.Rename(command.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ClientCompanyResponse.From(company);
    }
}
