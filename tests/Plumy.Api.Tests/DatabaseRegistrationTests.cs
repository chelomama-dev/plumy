using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Infrastructure.Persistence;

namespace Plumy.Api.Tests;

/// <summary>
/// The DbContext is registered by hand (no pooling, so it can receive the tenant) and enriched by Aspire.
/// These pin down what Aspire's registration used to give for free.
/// </summary>
[Collection(ApiCollection.Name)]
public class DatabaseRegistrationTests(PlumyApiFactory factory)
{
    [Fact]
    public void DbContext_RetriesTransientFailures()
    {
        // Azure SQL serverless pauses when idle: the first connection after a pause fails and must be retried
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlumyDbContext>();

        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void DbContext_IsNotPooled()
    {
        // A pool hands the instance of a finished request to the next one, along with the tenant it was built for
        PlumyDbContext first;
        using (var scope = factory.Services.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<PlumyDbContext>();
        }

        using var nextScope = factory.Services.CreateScope();
        Assert.NotSame(first, nextScope.ServiceProvider.GetRequiredService<PlumyDbContext>());
    }
}
