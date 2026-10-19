using Plumy.Application.Abstractions;
using Plumy.Application.Firms;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// Lets a request through only if the signed-in user belongs to a firm, and makes that firm the request's tenant.
/// A valid token isn't enough: users who haven't created or joined a firm get a 403.
/// </summary>
internal sealed class FirmMembershipFilter(
    ICurrentUser currentUser, IFirmRepository firms, RequestTenantContext tenant) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var membership = await firms.GetMembershipAsync(
            currentUser.ObjectId, context.HttpContext.RequestAborted);

        if (membership is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "No accounting firm",
                detail: "Create an accounting firm or accept an invitation to one first.");
        }

        tenant.SetFirm(membership.FirmId);

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

        return services;
    }

    /// <summary>Endpoints that read or write a firm's data. They run scoped to the user's firm.</summary>
    public static TBuilder RequireFirmMembership<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, FirmMembershipFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
