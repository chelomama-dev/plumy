using Plumy.Domain.Common;

namespace Plumy.Domain.ClientCompanies;

/// <summary>
/// A company whose books are kept by the accounting firm.
/// </summary>
public sealed class ClientCompany
{
    public const int MaxNameLength = 200;

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public TaxId TaxId { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    private ClientCompany(Guid id, string name, TaxId taxId)
    {
        Id = id;
        Name = name;
        TaxId = taxId;
    }

    public static ClientCompany Create(string? name, TaxId taxId) =>
        new(Guid.CreateVersion7(), ValidateName(name), taxId);

    public void Rename(string? name)
    {
        EnsureNotArchived();
        Name = ValidateName(name);
    }

    // Soft delete: the company keeps existing for its accounting history, it just stops being active.
    // The time comes from outside so the domain stays deterministic and testable
    public void Archive(DateTimeOffset archivedAt)
    {
        EnsureNotArchived();
        ArchivedAt = archivedAt;
    }

    private void EnsureNotArchived()
    {
        if (IsArchived)
            throw new DomainException("The client company is archived.");
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Client company name is required.");

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
            throw new DomainException($"Client company name cannot exceed {MaxNameLength} characters.");

        return trimmed;
    }
}
