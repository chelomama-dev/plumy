using Plumy.Application.Abstractions;

namespace Plumy.Application.ClientCompanies.ArchiveClientCompany;

public sealed class ArchiveClientCompanyHandler(
    IClientCompanyRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    // Returns false when there is nothing to archive
    public async Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetByIdAsync(id, cancellationToken);
        if (company is null)
            return false;

        company.Archive(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
