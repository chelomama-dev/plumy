using Plumy.Application.ClientCompanies.RenameClientCompany;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.Tests.ClientCompanies;

public class RenameClientCompanyHandlerTests
{
    private readonly InMemoryClientCompanyStore _store = new();
    private readonly RenameClientCompanyHandler _handler;

    public RenameClientCompanyHandlerTests() => _handler = new RenameClientCompanyHandler(_store, _store);

    [Fact]
    public async Task HandleAsync_WhenCompanyExists_RenamesSavesAndReturnsIt()
    {
        var company = await SeedAsync();

        var response = await _handler.HandleAsync(new RenameClientCompanyCommand(company.Id, " Acme Global S.A. "));

        Assert.NotNull(response);
        Assert.Equal("Acme Global S.A.", response.Name);
        Assert.Equal("Acme Global S.A.", company.Name);
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCompanyDoesNotExist_ReturnsNullAndDoesNotSave()
    {
        var response = await _handler.HandleAsync(new RenameClientCompanyCommand(Guid.NewGuid(), "Acme"));

        Assert.Null(response);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCompanyIsArchived_ReturnsNull()
    {
        var company = await SeedAsync();
        company.Archive(DateTimeOffset.UtcNow);

        var response = await _handler.HandleAsync(new RenameClientCompanyCommand(company.Id, "Acme Global S.A."));

        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidName_ThrowsDomainExceptionAndDoesNotSave()
    {
        var company = await SeedAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => _handler.HandleAsync(new RenameClientCompanyCommand(company.Id, "  ")));

        Assert.Equal(1, _store.SaveCount);
    }

    // Seeding counts as the first save
    private async Task<ClientCompany> SeedAsync()
    {
        var company = ClientCompany.Create(TestTenant.FirmId, "Acme S.L.", TaxId.Create("B12345674"));
        _store.Add(company);
        await _store.SaveChangesAsync();
        return company;
    }
}
