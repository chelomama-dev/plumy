namespace Plumy.Application.ClientCompanies.GetClientCompanyById;

public sealed class GetClientCompanyByIdHandler(IClientCompanyRepository repository)
{
    public async Task<ClientCompanyResponse?> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetByIdAsync(id, cancellationToken);

        return company is null ? null : ClientCompanyResponse.From(company);
    }
}
