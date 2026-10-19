namespace Plumy.Domain.Firms;

public enum FirmRole
{
    /// <summary>Created the firm. Manages its users (invitations, roles).</summary>
    Owner,

    /// <summary>Works with the firm's client companies.</summary>
    Member
}
