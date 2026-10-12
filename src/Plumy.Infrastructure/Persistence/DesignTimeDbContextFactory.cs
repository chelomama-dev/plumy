using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Plumy.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling. At runtime Aspire provides the real connection string.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlumyDbContext>
{
    public PlumyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlumyDbContext>()
            .UseSqlServer("Server=localhost;Database=plumydb")
            .Options;

        return new PlumyDbContext(options);
    }
}
