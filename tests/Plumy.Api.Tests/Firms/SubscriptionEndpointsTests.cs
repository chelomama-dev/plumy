using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.Billing;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class SubscriptionEndpointsTests(PlumyApiFactory factory)
{
    private const string Url = "/api/firm/subscription";

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string text)
    {
        var company = await client.PostAsJsonAsync("/api/client-companies", new { name = "Acme S.L.", taxId = TaxIds.NewNif() });
        var companyId = (await company.Content.ReadFromJsonAsync<ClientCompanyResponse>())!.Id;

        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. System.Text.Encoding.UTF8.GetBytes(text)]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return await client.PostAsync(
            $"/api/client-companies/{companyId}/invoices", new MultipartFormDataContent { { file, "file", "f.pdf" } });
    }

    [Fact]
    public async Task A_new_firm_is_on_a_fourteen_day_trial()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        var subscription = (await owner.GetFromJsonAsync<SubscriptionResponse>(Url))!;

        Assert.Equal("Trial", subscription.Plan);
        Assert.Equal("Trialing", subscription.Status);
        Assert.Equal(TimeSpan.FromDays(14), subscription.PeriodEnd - subscription.PeriodStart);
        Assert.Equal(50, subscription.InvoiceLimit);
        Assert.Equal(0, subscription.UploadedInPeriod);
        Assert.Equal("None", subscription.UploadBlock);
    }

    [Fact]
    public async Task Uploads_count_towards_the_limit()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(owner, "una")).StatusCode);

        Assert.Equal(1, (await owner.GetFromJsonAsync<SubscriptionResponse>(Url))!.UploadedInPeriod);
    }

    [Fact]
    public async Task Once_the_trial_ends_uploads_are_refused_with_402()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var me = (await owner.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!;
        await factory.EndTrialAsync(me.Firm!.Id);

        var response = await UploadAsync(owner, "tarde");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("trial has ended", problem!.Detail);
        Assert.Equal("TrialEnded", (await owner.GetFromJsonAsync<SubscriptionResponse>(Url))!.UploadBlock);
    }

    [Fact]
    public async Task It_needs_a_firm()
    {
        var response = await factory.CreateClientWithoutFirm().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
