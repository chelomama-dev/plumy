using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Application.Tests.ClientCompanies;

/// <summary>
/// Mimics EF Core: Add only stages the entity, and it becomes visible after SaveChangesAsync.
/// </summary>
internal sealed class InMemoryClientCompanyStore : IClientCompanyRepository, IUnitOfWork
{
    private readonly List<ClientCompany> _pending = [];

    public List<ClientCompany> Saved { get; } = [];
    public int SaveCount { get; private set; }

    public Task<ClientCompany?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(c => c.Id == id));

    public Task<bool> ExistsByTaxIdAsync(TaxId taxId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.Any(c => c.TaxId == taxId));

    public void Add(ClientCompany company) => _pending.Add(company);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var count = _pending.Count;
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.FromResult(count);
    }
}
