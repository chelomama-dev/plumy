namespace Plumy.Infrastructure.Persistence;

/// <summary>
/// Names of the global query filters (EF Core 10), so each one can be disabled on its own.
/// </summary>
public static class QueryFilters
{
    public const string NotArchived = nameof(NotArchived);
}
