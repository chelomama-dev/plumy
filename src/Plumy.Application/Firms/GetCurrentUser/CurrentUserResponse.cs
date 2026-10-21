namespace Plumy.Application.Firms.GetCurrentUser;

/// <summary>
/// Who is signed in and their firm. <see cref="Firm"/> is null until they create or join one; meanwhile,
/// <see cref="Invitations"/> lists the firms that invited their email address.
/// </summary>
public sealed record CurrentUserResponse(
    string? Name, string? Email, FirmMembershipResponse? Firm, IReadOnlyList<PendingInvitation> Invitations);
