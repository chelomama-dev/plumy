namespace Plumy.Domain.Common;

/// <summary>
/// Thrown when a business rule of the domain is violated.
/// </summary>
public class DomainException(string message) : Exception(message);
