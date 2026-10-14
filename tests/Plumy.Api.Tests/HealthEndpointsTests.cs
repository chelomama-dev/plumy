using System.Net;
using Microsoft.AspNetCore.Hosting;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Infrastructure;

namespace Plumy.Api.Tests;

[Collection(ApiCollection.Name)]
public class HealthEndpointsTests(PlumyApiFactory factory)
{
    // Container Apps probes hit these in Azure, so they must exist outside Development too
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder => builder.UseEnvironment("Production"))
        .CreateClient();

    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task HealthEndpoint_InProduction_Returns200Healthy(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthEndpoints_WhenDatabaseIsUnreachable_OnlyReadinessFails()
    {
        // Nothing listens on port 1: readiness must go down (stop routing traffic here) but liveness must stay up,
        // otherwise Container Apps would keep restarting a replica whose only problem is the database
        var client = factory
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .UseSetting($"ConnectionStrings:{DependencyInjection.DatabaseName}",
                    "Server=127.0.0.1,1;Database=plumydb;User Id=sa;Password=x;Connect Timeout=1;TrustServerCertificate=true"))
            .CreateClient();

        var health = await client.GetAsync("/health");
        var alive = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }
}
