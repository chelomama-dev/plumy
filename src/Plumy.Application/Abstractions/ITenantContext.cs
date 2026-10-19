namespace Plumy.Application.Abstractions;

/// <summary>
/// The accounting firm the current request works for. Every read and write of tenant data is scoped to it.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current firm. Throws when the request hasn't been resolved to one, so a missing check fails closed
    /// instead of reading or writing without a tenant.
    /// </summary>
    Guid FirmId { get; }
}
