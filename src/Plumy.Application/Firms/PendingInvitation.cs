namespace Plumy.Application.Firms;

/// <summary>An invitation the signed-in user can accept, as they see it: which firm is inviting them.</summary>
public sealed record PendingInvitation(Guid Id, string FirmName, DateTimeOffset ExpiresAt);
