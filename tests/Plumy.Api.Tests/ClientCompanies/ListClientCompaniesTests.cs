using System.Net;
using System.Net.Http.Json;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;

namespace Plumy.Api.Tests.ClientCompanies;

/// <summary>
/// The database is shared with other test classes, so every test creates companies under a unique name
/// prefix and searches by it: that keeps counts and ordering deterministic.
/// </summary>
[Collection(ApiCollection.Name)]
public class ListClientCompaniesTests(PlumyApiFactory factory)
{
    private const string BasePath = "/api/client-companies";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly string _prefix = $"List-{Guid.NewGuid():N}"[..17];

    [Fact]
    public async Task List_ReturnsRequestedPageOrderedByName()
    {
        await CreateCompaniesAsync("C", "A", "E", "B", "D");

        var page = await GetPageAsync($"?search={_prefix}&page=2&pageSize=2");

        Assert.Equal(["C", "D"], page.Items.Select(c => c.Name[_prefix.Length..]));
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task List_PageBeyondTheEnd_ReturnsEmptyItemsWithTotals()
    {
        await CreateCompaniesAsync("A", "B");

        var page = await GetPageAsync($"?search={_prefix}&page=5&pageSize=10");

        Assert.Empty(page.Items);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task List_WithoutParameters_UsesFirstPageWithDefaultSize()
    {
        var page = await GetPageAsync("");

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task List_SearchByName_IsCaseInsensitiveAndMatchesAnywhere()
    {
        await CreateCompaniesAsync(" Talleres Pérez", " Panadería López");

        var page = await GetPageAsync($"?search={_prefix.ToUpperInvariant()}&pageSize=10");
        Assert.Equal(2, page.TotalCount);

        page = await GetPageAsync($"?search={Uri.EscapeDataString(_prefix + " talleres")}");
        Assert.Equal(" Talleres Pérez", Assert.Single(page.Items).Name[_prefix.Length..]);
    }

    [Fact]
    public async Task List_SearchByTaxId_MatchesAfterNormalization()
    {
        var taxId = TaxIds.NewNif();
        await CreateCompanyAsync($"{_prefix} Acme", taxId);

        // "12345678-z" style input must find "12345678Z"
        var search = $"{taxId[..8]}-{char.ToLowerInvariant(taxId[8])}";
        var page = await GetPageAsync($"?search={search}");

        Assert.Equal(taxId, Assert.Single(page.Items).TaxId);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task List_InvalidPaging_Returns400ProblemDetails(string query)
    {
        var response = await _client.GetAsync($"{BasePath}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<PagedResult<ClientCompanyResponse>> GetPageAsync(string query)
    {
        var response = await _client.GetAsync($"{BasePath}{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ClientCompanyResponse>>())!;
    }

    private async Task CreateCompaniesAsync(params string[] suffixes)
    {
        foreach (var suffix in suffixes)
            await CreateCompanyAsync(_prefix + suffix, TaxIds.NewNif());
    }

    private async Task CreateCompanyAsync(string name, string taxId)
    {
        var response = await _client.PostAsJsonAsync(BasePath, new { name, taxId });
        response.EnsureSuccessStatusCode();
    }
}
