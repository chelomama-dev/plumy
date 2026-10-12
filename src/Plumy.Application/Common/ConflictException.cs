namespace Plumy.Application.Common;

/// <summary>
/// Thrown when an operation clashes with the current state of the system (e.g. a duplicate tax ID).
/// </summary>
public class ConflictException(string message) : Exception(message);
