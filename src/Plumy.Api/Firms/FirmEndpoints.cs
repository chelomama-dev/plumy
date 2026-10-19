using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Application.Firms;
using Plumy.Application.Firms.CreateFirm;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Api.Firms;

/// <summary>
/// What a signed-in user can do before belonging to a firm: see who they are and create one.
/// No RequireFirmMembership here, on purpose.
/// </summary>
internal static class FirmEndpoints
{
    public static IEndpointRouteBuilder MapFirmEndpoints(this IEndpointRouteBuilder app)
    {
        // The SPA calls it after sign-in to decide between onboarding and the app
        app.MapGet("/api/me", GetCurrentUserAsync)
            .WithTags("Firms")
            .WithName("GetCurrentUser");

        app.MapPost("/api/firms", CreateAsync)
            .WithTags("Firms")
            .WithName("CreateFirm")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Ok<CurrentUserResponse>> GetCurrentUserAsync(
        GetCurrentUserHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    // No GET /api/firms/{id} yet, so the location is the user's own profile, which includes the firm
    private static async Task<Created<FirmMembershipResponse>> CreateAsync(
        CreateFirmCommand command, CreateFirmHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Created("/api/me", await handler.HandleAsync(command, cancellationToken));
}
