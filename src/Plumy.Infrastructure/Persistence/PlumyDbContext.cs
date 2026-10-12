using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;

namespace Plumy.Infrastructure.Persistence;

public sealed class PlumyDbContext(DbContextOptions<PlumyDbContext> options) : DbContext(options), IUnitOfWork
{
    // SQL Server error numbers for a duplicate key in a unique index / unique constraint
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public DbSet<ClientCompany> ClientCompanies => Set<ClientCompany>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException
                                           {
                                               Number: UniqueIndexViolation or UniqueConstraintViolation
                                           })
        {
            throw new ConflictException("The record conflicts with an existing one.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlumyDbContext).Assembly);
}
