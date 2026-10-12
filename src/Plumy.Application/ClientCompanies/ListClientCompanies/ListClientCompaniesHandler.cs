using Plumy.Application.Common;

namespace Plumy.Application.ClientCompanies.ListClientCompanies;

public sealed class ListClientCompaniesHandler(IClientCompanyRepository repository)
{
    public async Task<PagedResult<ClientCompanyResponse>> HandleAsync(
        ListClientCompaniesQuery query, CancellationToken cancellationToken = default)
    {
        var page = await repository.ListAsync(query.Search, query.Page, query.PageSize, cancellationToken);

        return page.Map(ClientCompanyResponse.From);
    }
}
