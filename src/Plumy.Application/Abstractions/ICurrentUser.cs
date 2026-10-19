namespace Plumy.Application.Abstractions;

/// <summary>The signed-in user, as the access token describes them.</summary>
public interface ICurrentUser
{
    /// <summary>Object id in Entra External ID (<c>oid</c> claim).</summary>
    Guid ObjectId { get; }

    string? Name { get; }

    string? Email { get; }
}
