using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Billing;

namespace Plumy.Api.Firms;

/// <summary>
/// Paying for a plan (ADR 0012). The owner is sent to Stripe's pages; Stripe's webhook brings the result back. The
/// plan itself is read from GET /api/firm/subscription.
/// </summary>
internal static class BillingEndpoints
{
    public const string WebhookPath = "/api/billing/stripe-webhook";

    // Stripe's events weigh a few KB; anything this big isn't one, and isn't worth hashing
    private const int MaxWebhookBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/firm/billing")
            .WithTags("Billing")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/checkout", CheckoutAsync)
            .WithName("StartCheckout")
            .RequireFirmOwner()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/portal", PortalAsync)
            .WithName("OpenBillingPortal")
            .RequireFirmOwner()
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Stripe calls it, not a user: no token. The signature is what proves who sent it
        app.MapPost(WebhookPath, WebhookAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    // 400 for an unknown plan, 409 if the firm already pays one
    private static async Task<Ok<BillingRedirectResponse>> CheckoutAsync(
        StartCheckoutCommand request, StartCheckoutHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));

    // 409 if the firm has never paid
    private static async Task<Ok<BillingRedirectResponse>> PortalAsync(
        OpenBillingPortalHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    // 200 once handled or ignored; 400 if the signature is wrong, which Stripe doesn't retry. Anything else (Stripe or
    // the database down) is a 500, and Stripe tries again for up to three days
    private static async Task<IResult> WebhookAsync(
        HttpRequest request, IBillingProvider billing, SyncSubscriptionHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxWebhookBytes)
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);

        // The signature covers the exact bytes Stripe sent: read them as they are, no model binding
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var billingEvent = billing.ReadEvent(payload, request.Headers["Stripe-Signature"]);
        if (billingEvent is not null)
            await handler.HandleAsync(billingEvent, cancellationToken);

        return TypedResults.Ok();
    }
}
