using Plumy.Application.Abstractions;
using Plumy.Application.Firms;
using Plumy.Domain.Billing;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Firms;

/// <summary>
/// Mimics EF Core: Add only stages the firm, and it becomes visible after SaveChangesAsync. Loaded firms are the
/// same instances, so changes to them count as tracked; <see cref="SaveCount"/> tells whether they were saved.
/// </summary>
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

    public Task<FirmUser?> GetUserAsync(Guid userObjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SelectMany(f => f.Users).SingleOrDefault(u => u.ObjectId == userObjectId));

    public Task<IReadOnlyList<FirmUser>> ListUsersAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FirmUser>>(Saved.Single(f => f.Id == firmId).Users.ToList());

    public Task<Firm?> GetAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f => f.Id == firmId));

    public Task<AutoApprovalPolicy> GetAutoApprovalAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f => f.Id == firmId)?.AutoApproval ?? AutoApprovalPolicy.Disabled);

    /// <summary>Subscriptions of firms that aren't stored here, such as the test tenant's.</summary>
    public Dictionary<Guid, FirmSubscription> Subscriptions { get; } = [];

    public Task<FirmSubscription?> GetSubscriptionAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f => f.Id == firmId)?.Subscription
            ?? Subscriptions.GetValueOrDefault(firmId));

    public Task<Firm?> GetWithUsersAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f => f.Id == firmId));

    public Task<Firm?> GetWithOpenInvitationsAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f => f.Id == firmId));

    public Task<Firm?> GetByOpenInvitationAsync(
        Guid invitationId, EmailAddress email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(f =>
            f.Invitations.Any(i => i.Id == invitationId && i.IsOpen && i.Email == email)));

    public Task<IReadOnlyList<PendingInvitation>> ListPendingInvitationsAsync(
        EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PendingInvitation>>(Saved
            .SelectMany(f => f.Invitations, (f, i) => new { Firm = f, Invitation = i })
            .Where(x => x.Invitation.IsOpen && x.Invitation.Email == email && !x.Invitation.IsExpired(now))
            .Select(x => new PendingInvitation(x.Invitation.Id, x.Firm.Name, x.Invitation.ExpiresAt))
            .ToList());

    public void Add(Firm firm) => _pending.Add(firm);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var count = _pending.Count;
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.FromResult(count);
    }

    /// <summary>Stores a firm as if it had been created earlier, without counting it as a save.</summary>
    public Firm Seed(string name, Guid ownerObjectId, UserProfile? ownerProfile = null)
    {
        var firm = Firm.Create(name, ownerObjectId, DateTimeOffset.UtcNow, ownerProfile);
        Saved.Add(firm);
        return firm;
    }
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid ObjectId { get; init; } = Guid.NewGuid();
    public string? Name { get; set; } = "Ana López";
    public string? Email { get; set; } = "ana@gestorialopez.es";
}

internal sealed class FakeTenant(Guid firmId) : ITenantContext
{
    public Guid FirmId => firmId;
}

/// <summary>A clock the test moves by hand, to reach an invitation's expiry.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
