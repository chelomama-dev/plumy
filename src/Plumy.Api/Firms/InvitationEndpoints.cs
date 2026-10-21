using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Firms;
using Plumy.Application.Firms.AcceptInvitation;
using Plumy.Application.Firms.InviteMember;
using Plumy.Application.Firms.ListInvitations;
using Plumy.Application.Firms.RevokeInvitation;

namespace Plumy.Api.Firms;

/// <summary>
/// Inviting people to a firm. The owner manages the invitations of their firm; the invited person, who has no
/// firm yet, finds theirs in /api/me and accepts one here.
/// </summary>
internal static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // "firm", singular: always the current user's own firm, never one picked by id
        var owner = app.MapGroup("/api/firm/invitations")
            .WithTags("Invitations")
            .RequireFirmOwner();

        owner.MapGet("/", ListAsync)
            .WithName("ListInvitations");

        // Not a 201: inviting an address that already has an open invitation renews that one instead
        owner.MapPost("/", InviteAsync)
            .WithName("InviteMember")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        owner.MapDelete("/{id:guid}", RevokeAsync)
            .WithName("RevokeInvitation");

        // No RequireFirmMembership: whoever accepts doesn't belong to a firm yet
        app.MapPost("/api/invitations/{id:guid}/accept", AcceptAsync)
            .WithTags("Invitations")
            .WithName("AcceptInvitation")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<InvitationResponse>>> ListAsync(
        ListInvitationsHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    private static async Task<Ok<InvitationResponse>> InviteAsync(
        InviteMemberCommand command, InviteMemberHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(command, cancellationToken));

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, RevokeInvitationHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    // 404 too when the invitation exists but was sent to someone else: its id alone reveals nothing
    private static async Task<Results<Ok<FirmMembershipResponse>, NotFound>> AcceptAsync(
        Guid id, AcceptInvitationHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } membership
            ? TypedResults.Ok(membership)
            : TypedResults.NotFound();
}
