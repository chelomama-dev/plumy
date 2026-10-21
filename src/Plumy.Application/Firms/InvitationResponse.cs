using Plumy.Domain.Firms;

namespace Plumy.Application.Firms;

/// <summary>An invitation as the firm's owner sees it.</summary>
public sealed record InvitationResponse(
    Guid Id, string Email, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsExpired)
{
    // Expiry is decided with the server's clock, so a wrong clock in the browser can't show it otherwise
    public static InvitationResponse From(FirmInvitation invitation, DateTimeOffset now) =>
        new(invitation.Id, invitation.Email.Value, invitation.CreatedAt, invitation.ExpiresAt,
            invitation.IsExpired(now));
}
