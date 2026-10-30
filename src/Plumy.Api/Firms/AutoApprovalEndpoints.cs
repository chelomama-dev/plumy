using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Firms.AutoApproval;

namespace Plumy.Api.Firms;

/// <summary>
/// Whether Plumy approves by itself the invoices it is sure about. Everyone in the firm can see it; only the owner
/// changes it.
/// </summary>
internal static class AutoApprovalEndpoints
{
    public static IEndpointRouteBuilder MapAutoApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/firm/auto-approval")
            .WithTags("Auto-approval");

        group.MapGet("/", GetAsync)
            .WithName("GetAutoApproval")
            .RequireFirmMembership();

        // The body is the command: both fields, the whole setting
        group.MapPut("/", SetAsync)
            .WithName("SetAutoApproval")
            .RequireFirmOwner()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<AutoApprovalResponse>> GetAsync(
        GetAutoApprovalHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    // 400 when the confidence is out of range
    private static async Task<Ok<AutoApprovalResponse>> SetAsync(
        SetAutoApprovalCommand request, SetAutoApprovalHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
