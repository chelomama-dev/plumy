using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Firms;
using Plumy.Application.Firms.ListMembers;
using Plumy.Application.Firms.RemoveMember;

namespace Plumy.Api.Firms;

/// <summary>The people of the current user's firm. Everyone in it can see them; only the owner removes them.</summary>
internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/firm/members")
            .WithTags("Members");

        group.MapGet("/", ListAsync)
            .WithName("ListMembers")
            .RequireFirmMembership();

        group.MapDelete("/{id:guid}", RemoveAsync)
            .WithName("RemoveMember")
            .RequireFirmOwner()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<MemberResponse>>> ListAsync(
        ListMembersHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    // 400 when it's the owner: the firm must keep one
    private static async Task<Results<NoContent, NotFound>> RemoveAsync(
        Guid id, RemoveMemberHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
}
