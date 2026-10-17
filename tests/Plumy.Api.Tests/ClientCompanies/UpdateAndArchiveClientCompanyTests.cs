using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;

namespace Plumy.Api.Tests.ClientCompanies;

[Collection(ApiCollection.Name)]
public class UpdateAndArchiveClientCompanyTests(PlumyApiFactory factory)
{
    private const string BasePath = "/api/client-companies";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Put_WithValidName_Returns200AndPersistsTheChange()
    {
        var company = await CreateCompanyAsync();

        var response = await _client.PutAsJsonAsync($"{BasePath}/{company.Id}", new { name = "Acme Global S.A." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Acme Global S.A.", (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!.Name);

        var reloaded = await _client.GetFromJsonAsync<ClientCompanyResponse>($"{BasePath}/{company.Id}");
        Assert.Equal(company with { Name = "Acme Global S.A." }, reloaded);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"{BasePath}/{Guid.NewGuid()}", new { name = "Acme" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidName_Returns400ProblemDetails()
    {
        var company = await CreateCompanyAsync();

        var response = await _client.PutAsJsonAsync($"{BasePath}/{company.Id}", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Delete_Returns204AndHidesTheCompany()
    {
        var company = await CreateCompanyAsync();

        var response = await _client.DeleteAsync($"{BasePath}/{company.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{BasePath}/{company.Id}")).StatusCode);

        var list = await _client.GetFromJsonAsync<PagedResult<ClientCompanyResponse>>($"{BasePath}?search={company.TaxId}");
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Delete_Twice_Returns404TheSecondTime()
    {
        var company = await CreateCompanyAsync();
        await _client.DeleteAsync($"{BasePath}/{company.Id}");

        var response = await _client.DeleteAsync($"{BasePath}/{company.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ArchivedCompany_Returns404()
    {
        var company = await CreateCompanyAsync();
        await _client.DeleteAsync($"{BasePath}/{company.Id}");

        var response = await _client.PutAsJsonAsync($"{BasePath}/{company.Id}", new { name = "Acme Global S.A." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithTaxIdOfArchivedCompany_Returns409MentioningArchived()
    {
        var company = await CreateCompanyAsync();
        await _client.DeleteAsync($"{BasePath}/{company.Id}");

        var response = await _client.PostAsJsonAsync(BasePath, new { name = "Acme again", taxId = company.TaxId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("archived", problem!.Detail);
    }

    private async Task<ClientCompanyResponse> CreateCompanyAsync()
    {
        var response = await _client.PostAsJsonAsync(BasePath, new { name = "Acme S.L.", taxId = TaxIds.NewNif() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }
}
