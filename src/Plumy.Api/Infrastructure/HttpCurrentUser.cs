using System.Security.Claims;
using Plumy.Application.Abstractions;

namespace Plumy.Api.Infrastructure;

/// <summary>Reads the signed-in user from the validated access token of the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User =>
        httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("There is no current request.");

    // Every token that passes the authorization policy is a delegated user token, so it always has an oid
    public Guid ObjectId => Guid.TryParse(User.FindFirstValue("oid"), out var objectId)
        ? objectId
        : throw new InvalidOperationException("The access token has no valid 'oid' claim.");

    public string? Name => User.FindFirstValue("name");

    public string? Email => User.FindFirstValue("email");
}
