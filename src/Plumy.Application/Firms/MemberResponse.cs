using Plumy.Domain.Firms;

namespace Plumy.Application.Firms;

/// <summary>Someone in the firm, as their team sees them. Name and email are null until they sign in again.</summary>
public sealed record MemberResponse(Guid Id, string? Name, string? Email, string Role, bool IsCurrentUser)
{
    public static MemberResponse From(FirmUser user, Guid currentUserObjectId) =>
        new(user.Id, user.Name, user.Email?.Value, user.Role.ToString(), user.ObjectId == currentUserObjectId);
}
