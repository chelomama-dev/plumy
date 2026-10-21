using Plumy.Application.Abstractions;
using Plumy.Application.Firms;
using Plumy.Domain.Firms;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// Lets a request through only if the signed-in user belongs to a firm, and makes that firm the request's tenant.
/// A valid token isn't enough: users who haven't created or joined a firm get a 403.
/// </summary>
internal sealed class FirmMembershipFilter(
    ICurrentUser currentUser, IFirmRepository firms, RequestTenantContext tenant) : IEndpointFilter
{
    public const string NoFirmProblemType = "urn:plumy:problem:no-firm";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var membership = await firms.GetMembershipAsync(
            currentUser.ObjectId, context.HttpContext.RequestAborted);

        if (membership is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                // Stable id for clients: the SPA sends the user back to onboarding on it (e.g. after being removed)
                type: NoFirmProblemType,
                title: "No accounting firm",
                detail: "Create an accounting firm or accept an invitation to one first.");
        }

        tenant.SetMembership(membership);

        return await next(context);
    }
}

/// <summary>
/// Lets a request through only if the user is the owner of their firm. Runs after <see cref="FirmMembershipFilter"/>,
/// which has already resolved the role: no extra query.
/// </summary>
internal sealed class FirmOwnerFilter(RequestTenantContext tenant) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (tenant.Role != FirmRole.Owner)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Owner only",
                detail: "Only the firm's owner can do this.");
        }

        return await next(context);
    }
}

internal static class FirmMembershipExtensions
{
    public static IServiceCollection AddFirmMembership(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // One instance per request: the filter writes it, the DbContext and the handlers read it
        services.AddScoped<RequestTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenantContext>());
        services.AddScoped<FirmMembershipFilter>();
        services.AddScoped<FirmOwnerFilter>();

        return services;
    }

    /// <summary>Endpoints that read or write a firm's data. They run scoped to the user's firm.</summary>
    public static TBuilder RequireFirmMembership<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, FirmMembershipFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

    /// <summary>Endpoints that manage the firm itself (its people). Only its owner can call them.</summary>
    public static TBuilder RequireFirmOwner<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        // Filters run in the order they're added: membership first, so the role is known when the owner check runs
        builder.RequireFirmMembership().AddEndpointFilter<TBuilder, FirmOwnerFilter>();
}
