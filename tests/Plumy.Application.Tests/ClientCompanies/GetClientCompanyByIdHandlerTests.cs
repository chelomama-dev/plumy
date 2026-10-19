using Plumy.Application.ClientCompanies.GetClientCompanyById;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.Tests.ClientCompanies;

public class GetClientCompanyByIdHandlerTests
{
    private readonly InMemoryClientCompanyStore _store = new();
    private readonly GetClientCompanyByIdHandler _handler;

    public GetClientCompanyByIdHandlerTests() => _handler = new GetClientCompanyByIdHandler(_store);

    [Fact]
    public async Task HandleAsync_WhenCompanyExists_ReturnsIt()
    {
        var company = ClientCompany.Create(TestTenant.FirmId, "Acme S.L.", TaxId.Create("B12345674"));
        _store.Add(company);
        await _store.SaveChangesAsync();

        var response = await _handler.HandleAsync(company.Id);

        Assert.NotNull(response);
        Assert.Equal(company.Id, response.Id);
        Assert.Equal("Acme S.L.", response.Name);
    }

    [Fact]
    public async Task HandleAsync_WhenCompanyDoesNotExist_ReturnsNull()
    {
        var response = await _handler.HandleAsync(Guid.NewGuid());

        Assert.Null(response);
    }
}
