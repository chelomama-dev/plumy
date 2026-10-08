using Microsoft.EntityFrameworkCore;
using Plumy.Domain.ClientCompanies;

namespace Plumy.Infrastructure.Persistence;

public sealed class PlumyDbContext(DbContextOptions<PlumyDbContext> options) : DbContext(options)
{
    public DbSet<ClientCompany> ClientCompanies => Set<ClientCompany>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlumyDbContext).Assembly);
}
