using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Application.ClientCompanies;
using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.ClientCompanies.GetClientCompanyById;

namespace Plumy.Api.ClientCompanies;

internal static class ClientCompanyEndpoints
{
    private const string BasePath = "/api/client-companies";

    public static IEndpointRouteBuilder MapClientCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("ClientCompanies");

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientCompany")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientCompanyById");

        return app;
    }

    private static async Task<Created<ClientCompanyResponse>> CreateAsync(
        CreateClientCompanyCommand command, CreateClientCompanyHandler handler, CancellationToken cancellationToken)
    {
        var company = await handler.HandleAsync(command, cancellationToken);

        return TypedResults.Created($"{BasePath}/{company.Id}", company);
    }

    private static async Task<Results<Ok<ClientCompanyResponse>, NotFound>> GetByIdAsync(
        Guid id, GetClientCompanyByIdHandler handler, CancellationToken cancellationToken)
    {
        var company = await handler.HandleAsync(id, cancellationToken);

        return company is null ? TypedResults.NotFound() : TypedResults.Ok(company);
    }
}
