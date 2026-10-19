using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.Tests.ClientCompanies;

public class CreateClientCompanyHandlerTests
{
    private readonly InMemoryClientCompanyStore _store = new();
    private readonly CreateClientCompanyHandler _handler;

    public CreateClientCompanyHandlerTests() => _handler = new CreateClientCompanyHandler(_store, _store, new TestTenant());

    [Fact]
    public async Task HandleAsync_WithValidData_SavesAndReturnsCompany()
    {
        var response = await _handler.HandleAsync(new CreateClientCompanyCommand("Acme S.L.", "B12345674"));

        var saved = Assert.Single(_store.Saved);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(saved.Id, response.Id);
        Assert.Equal(TestTenant.FirmId, saved.FirmId);
        Assert.Equal("Acme S.L.", response.Name);
        Assert.Equal("B12345674", response.TaxId);
        Assert.Equal("Cif", response.TaxIdType);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateTaxId_ThrowsConflictAndDoesNotSave()
    {
        await SeedAsync("Existing S.L.", "B12345674");

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(new CreateClientCompanyCommand("Acme S.L.", "B12345674")));

        Assert.Single(_store.Saved);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WithTaxIdOfArchivedCompany_ThrowsConflictMentioningArchived()
    {
        var existing = await SeedAsync("Existing S.L.", "B12345674");
        existing.Archive(DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(new CreateClientCompanyCommand("Acme S.L.", "B12345674")));

        Assert.Contains("archived", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_DetectsDuplicatesAfterNormalizingTaxId()
    {
        await SeedAsync("Existing S.L.", "B12345674");

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(new CreateClientCompanyCommand("Acme S.L.", " b-1234567.4 ")));
    }

    [Fact]
    public async Task HandleAsync_WithInvalidTaxId_ThrowsDomainExceptionAndDoesNotSave()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => _handler.HandleAsync(new CreateClientCompanyCommand("Acme S.L.", "B12345670")));

        Assert.Empty(_store.Saved);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidName_ThrowsDomainExceptionAndDoesNotSave()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => _handler.HandleAsync(new CreateClientCompanyCommand("  ", "B12345674")));

        Assert.Empty(_store.Saved);
    }

    private async Task<ClientCompany> SeedAsync(string name, string taxId)
    {
        var company = ClientCompany.Create(TestTenant.FirmId, name, TaxId.Create(taxId));
        _store.Add(company);
        await _store.SaveChangesAsync();
        return company;
    }
}
