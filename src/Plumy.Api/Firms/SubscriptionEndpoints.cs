using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Billing;

namespace Plumy.Api.Firms;

/// <summary>The firm's plan and how much of it is used. Changing plan goes through Stripe (ADR 0012).</summary>
internal static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/firm/subscription", GetAsync)
            .WithTags("Subscription")
            .WithName("GetSubscription")
            .RequireFirmMembership();

        return app;
    }

    private static async Task<Ok<SubscriptionResponse>> GetAsync(
        GetSubscriptionHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
