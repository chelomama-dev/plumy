using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;

namespace Plumy.Api.Tests.ClientCompanies;

[Collection(ApiCollection.Name)]
public class ClientCompanyEndpointsTests(PlumyApiFactory factory)
{
    private const string BasePath = "/api/client-companies";
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Post_WithValidData_Returns201WithLocationAndBody()
    {
        var taxId = TaxIds.NewNif();

        var response = await _client.PostAsJsonAsync(BasePath, new { name = "Acme S.L.", taxId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var company = await response.Content.ReadFromJsonAsync<ClientCompanyResponse>();
        Assert.NotNull(company);
        Assert.Equal("Acme S.L.", company.Name);
        Assert.Equal(taxId, company.TaxId);
        Assert.Equal("Nif", company.TaxIdType);
        Assert.Equal($"{BasePath}/{company.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Get_AfterCreate_ReturnsSameCompany()
    {
        var created = await CreateCompanyAsync("Acme S.L.", TaxIds.NewNif());

        var response = await _client.GetAsync($"{BasePath}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created, await response.Content.ReadFromJsonAsync<ClientCompanyResponse>());
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"{BasePath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_IdThatIsNotAGuid_Returns404()
    {
        // The {id:guid} route constraint doesn't match, so no endpoint is reached at all
        var response = await _client.GetAsync($"{BasePath}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateTaxId_Returns409ProblemDetails()
    {
        var taxId = TaxIds.NewNif();
        await CreateCompanyAsync("First S.L.", taxId);

        // Lowercase and with a dash: must still be detected as the same tax ID after normalization
        var duplicate = $"{taxId[..8]}-{char.ToLowerInvariant(taxId[8])}";
        var response = await _client.PostAsJsonAsync(BasePath, new { name = "Second S.L.", taxId = duplicate });

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Contains(taxId, problem.Detail);
    }

    [Theory]
    [InlineData("Acme S.L.", "B12345670")] // wrong check digit
    [InlineData("Acme S.L.", null)]
    [InlineData("   ", "B12345674")]
    [InlineData(null, "B12345674")]
    public async Task Post_InvalidData_Returns400ProblemDetails(string? name, string? taxId)
    {
        var response = await _client.PostAsJsonAsync(BasePath, new { name, taxId });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MalformedJson_Returns400ProblemDetails()
    {
        var response = await _client.PostAsync(BasePath, new StringContent("{\"name\":", null, "application/json"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private async Task<ClientCompanyResponse> CreateCompanyAsync(string name, string taxId)
    {
        var response = await _client.PostAsJsonAsync(BasePath, new { name, taxId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }

    private static async Task<ProblemDetails> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)status, problem.Status);
        return problem;
    }
}
