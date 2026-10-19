using Plumy.Application.Abstractions;
using Plumy.Application.Firms;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Firms;

/// <summary>Mimics EF Core: Add only stages the firm, and it becomes visible after SaveChangesAsync.</summary>
internal sealed class InMemoryFirmStore : IFirmRepository, IUnitOfWork
{
    private readonly List<Firm> _pending = [];

    public List<Firm> Saved { get; } = [];
    public int SaveCount { get; private set; }

    public Task<FirmMembership?> GetMembershipAsync(Guid userObjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved
            .SelectMany(f => f.Users, (f, u) => new { Firm = f, User = u })
            .Where(x => x.User.ObjectId == userObjectId)
            .Select(x => new FirmMembership(x.Firm.Id, x.Firm.Name, x.User.Role))
            .SingleOrDefault());

    public void Add(Firm firm) => _pending.Add(firm);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var count = _pending.Count;
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.FromResult(count);
    }
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid ObjectId { get; init; } = Guid.NewGuid();
    public string? Name { get; init; } = "Ana López";
    public string? Email { get; init; } = "ana@gestorialopez.es";
}
