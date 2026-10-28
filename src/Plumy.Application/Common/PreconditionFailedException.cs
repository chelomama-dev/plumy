namespace Plumy.Application.Common;

/// <summary>
/// The client changed something from a copy that is no longer current (its version doesn't match). The API answers
/// 412 Precondition Failed: the client should load it again before retrying.
/// </summary>
public class PreconditionFailedException(string message) : Exception(message);
