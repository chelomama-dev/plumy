namespace Plumy.Application.Abstractions;

/// <summary>
/// Commits every change tracked during the current request in a single transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
