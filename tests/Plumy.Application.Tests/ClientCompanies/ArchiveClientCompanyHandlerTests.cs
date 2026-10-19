using Plumy.Application.ClientCompanies.ArchiveClientCompany;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.Tests.ClientCompanies;

public class ArchiveClientCompanyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 30, 0, TimeSpan.Zero);

    private readonly InMemoryClientCompanyStore _store = new();
    private readonly ArchiveClientCompanyHandler _handler;

    public ArchiveClientCompanyHandlerTests() =>
        _handler = new ArchiveClientCompanyHandler(_store, _store, new FixedTimeProvider(Now));

    [Fact]
    public async Task HandleAsync_WhenCompanyExists_ArchivesWithCurrentTimeAndSaves()
    {
        var company = ClientCompany.Create(TestTenant.FirmId, "Acme S.L.", TaxId.Create("B12345674"));
        _store.Add(company);
        await _store.SaveChangesAsync();

        var found = await _handler.HandleAsync(company.Id);

        Assert.True(found);
        Assert.Equal(Now, company.ArchivedAt);
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCompanyDoesNotExist_ReturnsFalseAndDoesNotSave()
    {
        var found = await _handler.HandleAsync(Guid.NewGuid());

        Assert.False(found);
        Assert.Equal(0, _store.SaveCount);
    }
}
