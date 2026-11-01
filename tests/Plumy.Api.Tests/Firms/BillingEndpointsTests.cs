using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.Billing;
using Plumy.Application.Firms;
using Plumy.Application.Firms.GetCurrentUser;
using Plumy.Domain.Billing;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class BillingEndpointsTests(PlumyApiFactory factory)
{
    private const string CheckoutUrl = "/api/firm/billing/checkout";
    private const string PortalUrl = "/api/firm/billing/portal";
    private const string WebhookUrl = "/api/billing/stripe-webhook";

    private static async Task<Guid> FirmIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Firm!.Id;

    private async Task<HttpResponseMessage> SendWebhookAsync(string subscriptionId, string signature)
    {
        // Stripe has no token: an anonymous client
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl) { Content = new StringContent(subscriptionId) };
        request.Headers.Add("Stripe-Signature", signature);
        return await factory.CreateClient().SendAsync(request);
    }

    /// <summary>The owner goes through Checkout and Stripe says the subscription exists.</summary>
    private async Task SubscribeAsync(HttpClient owner, string subscriptionId, Plan plan, SubscriptionStatus status)
    {
        (await owner.PostAsJsonAsync(CheckoutUrl, new { plan = plan.ToString() })).EnsureSuccessStatusCode();
        var now = DateTimeOffset.UtcNow;
        factory.Billing.Subscriptions[subscriptionId] = new PaidSubscriptionState(
            $"cus_{await FirmIdAsync(owner):N}",
            FirmSubscription.Paid(subscriptionId, plan, status, now.AddDays(-1), now.AddDays(29)));

        (await SendWebhookAsync(subscriptionId, FakeBillingProvider.ValidSignature)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Checkout_SendsTheOwnerToStripe_WithoutChangingThePlanYet()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        var response = await owner.PostAsJsonAsync(CheckoutUrl, new { plan = "Pro" });

        response.EnsureSuccessStatusCode();
        var redirect = (await response.Content.ReadFromJsonAsync<BillingRedirectResponse>())!;
        Assert.Equal($"https://checkout.stripe.test/cus_{await FirmIdAsync(owner):N}/Pro", redirect.Url.ToString());
        Assert.Equal("Trial", (await owner.GetFromJsonAsync<SubscriptionResponse>("/api/firm/subscription"))!.Plan);
    }

    [Fact]
    public async Task Checkout_ForTheTrialPlan_Is400()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        var response = await owner.PostAsJsonAsync(CheckoutUrl, new { plan = "Trial" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Billing_IsForTheOwnerOnly()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        var invitation = await (await owner.PostAsJsonAsync("/api/firm/invitations", new { email }))
            .Content.ReadFromJsonAsync<InvitationResponse>();
        var member = factory.CreateClientWithoutFirm(email);
        (await member.PostAsync($"/api/invitations/{invitation!.Id}/accept", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(CheckoutUrl, new { plan = "Basic" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync(PortalUrl, null)).StatusCode);
    }

    [Fact]
    public async Task Portal_BeforePaying_Is409()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(PortalUrl, null)).StatusCode);
    }

    [Fact]
    public async Task Webhook_PutsTheFirmOnThePlanItPaidFor()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        await SubscribeAsync(owner, $"sub_{Guid.NewGuid():N}", Plan.Pro, SubscriptionStatus.Active);

        var subscription = (await owner.GetFromJsonAsync<SubscriptionResponse>("/api/firm/subscription"))!;
        Assert.Equal("Pro", subscription.Plan);
        Assert.Equal("Active", subscription.Status);
        Assert.Equal(1500, subscription.InvoiceLimit);
        Assert.Equal("None", subscription.UploadBlock);
    }

    [Fact]
    public async Task Webhook_ACanceledSubscription_StopsUploads()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        await SubscribeAsync(owner, subscriptionId, Plan.Basic, SubscriptionStatus.Active);

        var state = factory.Billing.Subscriptions[subscriptionId];
        factory.Billing.Subscriptions[subscriptionId] = state with
        {
            Subscription = FirmSubscription.Paid(subscriptionId, Plan.Basic, SubscriptionStatus.Canceled,
                state.Subscription.PeriodStart, state.Subscription.PeriodEnd)
        };
        (await SendWebhookAsync(subscriptionId, FakeBillingProvider.ValidSignature)).EnsureSuccessStatusCode();

        Assert.Equal("NotSubscribed",
            (await owner.GetFromJsonAsync<SubscriptionResponse>("/api/firm/subscription"))!.UploadBlock);
    }

    [Fact]
    public async Task Once_paying_checkout_is_409_and_the_portal_opens()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        await SubscribeAsync(owner, $"sub_{Guid.NewGuid():N}", Plan.Basic, SubscriptionStatus.Active);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(CheckoutUrl, new { plan = "Pro" })).StatusCode);

        var portal = await owner.PostAsync(PortalUrl, null);
        portal.EnsureSuccessStatusCode();
        Assert.Equal($"https://billing.stripe.test/cus_{await FirmIdAsync(owner):N}",
            (await portal.Content.ReadFromJsonAsync<BillingRedirectResponse>())!.Url.ToString());
    }

    [Fact]
    public async Task Webhook_WithABadSignature_Is400()
    {
        var response = await SendWebhookAsync("sub_whatever", "t=1,v1=forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid webhook", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Webhook_ForASubscriptionNotPaidYet_Is200AndChangesNothing()
    {
        var response = await SendWebhookAsync($"sub_{Guid.NewGuid():N}", FakeBillingProvider.ValidSignature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithoutToken_Is401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(CheckoutUrl, new { plan = "Basic" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
