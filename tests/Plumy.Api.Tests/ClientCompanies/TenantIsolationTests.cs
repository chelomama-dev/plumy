using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;

namespace Plumy.Api.Tests.ClientCompanies;

/// <summary>
/// The promise of multi-tenancy: a firm never sees or touches another firm's data, whatever id it sends.
/// From outside, another firm's company is indistinguishable from one that doesn't exist (404, never 403).
/// </summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private const string BasePath = "/api/client-companies";

    private HttpClient _firmA = null!;
    private HttpClient _firmB = null!;
    private ClientCompanyResponse _companyOfA = null!;

    public async Task InitializeAsync()
    {
        _firmA = await factory.CreateClientInNewFirmAsync("Gestoría A");
        _firmB = await factory.CreateClientInNewFirmAsync("Gestoría B");

        var response = await _firmA.PostAsJsonAsync(BasePath, new { name = "Cliente de A S.L.", taxId = TaxIds.NewNif() });
        response.EnsureSuccessStatusCode();
        _companyOfA = (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetById_OtherFirmsCompany_Returns404()
    {
        var response = await _firmB.GetAsync($"{BasePath}/{_companyOfA.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_OnlyReturnsOwnFirmsCompanies()
    {
        var ofA = await _firmA.GetFromJsonAsync<PagedResult<ClientCompanyResponse>>(BasePath);
        var ofB = await _firmB.GetFromJsonAsync<PagedResult<ClientCompanyResponse>>(BasePath);

        Assert.Equal([_companyOfA.Id], ofA!.Items.Select(c => c.Id));
        Assert.Empty(ofB!.Items);
        Assert.Equal(0, ofB.TotalCount);
    }

    [Fact]
    public async Task Search_ByOtherFirmsTaxId_FindsNothing()
    {
        var result = await _firmB.GetFromJsonAsync<PagedResult<ClientCompanyResponse>>(
            $"{BasePath}?search={_companyOfA.TaxId}");

        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Rename_OtherFirmsCompany_Returns404AndChangesNothing()
    {
        var response = await _firmB.PutAsJsonAsync($"{BasePath}/{_companyOfA.Id}", new { name = "Hackeada S.L." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(_companyOfA, await _firmA.GetFromJsonAsync<ClientCompanyResponse>($"{BasePath}/{_companyOfA.Id}"));
    }

    [Fact]
    public async Task Archive_OtherFirmsCompany_Returns404AndChangesNothing()
    {
        var response = await _firmB.DeleteAsync($"{BasePath}/{_companyOfA.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _firmA.GetAsync($"{BasePath}/{_companyOfA.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_SameTaxIdAsOtherFirm_Returns201()
    {
        // Two firms can keep the books of the same company; uniqueness is per firm
        var response = await _firmB.PostAsJsonAsync(BasePath, new { name = "Cliente de A S.L.", taxId = _companyOfA.TaxId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ClientCompanies_UserWithoutFirm_Returns403ProblemDetails()
    {
        var response = await factory.CreateClientWithoutFirm().GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("No accounting firm", problem?.Title);
    }
}
