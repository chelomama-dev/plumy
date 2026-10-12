using Plumy.Domain.ClientCompanies;

namespace Plumy.Application.ClientCompanies;

public sealed record ClientCompanyResponse(Guid Id, string Name, string TaxId, string TaxIdType)
{
    public static ClientCompanyResponse From(ClientCompany company) =>
        new(company.Id, company.Name, company.TaxId.Value, company.TaxId.Type.ToString());
}
