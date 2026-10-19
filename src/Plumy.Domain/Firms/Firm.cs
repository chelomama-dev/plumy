using Plumy.Domain.Common;

namespace Plumy.Domain.Firms;

/// <summary>
/// An accounting firm (gestoría): the tenant. Every client company and every user belongs to exactly one.
/// </summary>
public sealed class Firm
{
    public const int MaxNameLength = 200;

    private readonly List<FirmUser> _users = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public IReadOnlyCollection<FirmUser> Users => _users.AsReadOnly();

    private Firm(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Self-service onboarding: whoever creates the firm becomes its owner.</summary>
    public static Firm Create(string? name, Guid ownerObjectId)
    {
        var firm = new Firm(Guid.CreateVersion7(), ValidateName(name));
        firm._users.Add(FirmUser.Create(firm.Id, ownerObjectId, FirmRole.Owner));
        return firm;
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Firm name is required.");

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
            throw new DomainException($"Firm name cannot exceed {MaxNameLength} characters.");

        return trimmed;
    }
}
