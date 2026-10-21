using Plumy.Application.Abstractions;
using Plumy.Domain.Firms;

namespace Plumy.Application.Firms.ListMembers;

/// <summary>The current firm's people: the owner first, then everyone else by name. Any member can see it.</summary>
public sealed class ListMembersHandler(ITenantContext tenant, ICurrentUser currentUser, IFirmRepository repository)
{
    public async Task<IReadOnlyList<MemberResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var users = await repository.ListUsersAsync(tenant.FirmId, cancellationToken);

        // A firm has a handful of people: sorting in memory keeps the culture-aware order (á next to a)
        return users
            .OrderBy(u => u.Role == FirmRole.Owner ? 0 : 1)
            .ThenBy(u => u.Name is null)
            .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Email?.Value, StringComparer.Ordinal)
            .Select(u => MemberResponse.From(u, currentUser.ObjectId))
            .ToList();
    }
}
