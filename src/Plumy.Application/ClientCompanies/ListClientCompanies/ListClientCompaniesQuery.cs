namespace Plumy.Application.ClientCompanies.ListClientCompanies;

public sealed record ListClientCompaniesQuery(int Page, int PageSize, string? Search);
