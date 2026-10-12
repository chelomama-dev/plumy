using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Application.ClientCompanies;
using Plumy.Application.ClientCompanies.ArchiveClientCompany;
using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.ClientCompanies.GetClientCompanyById;
using Plumy.Application.ClientCompanies.ListClientCompanies;
using Plumy.Application.ClientCompanies.RenameClientCompany;
using Plumy.Application.Common;

namespace Plumy.Api.ClientCompanies;

internal static class ClientCompanyEndpoints
{
    private const string BasePath = "/api/client-companies";

    public static IEndpointRouteBuilder MapClientCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("ClientCompanies");

        group.MapGet("/", ListAsync)
            .WithName("ListClientCompanies")
            .ProducesValidationProblem();

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientCompany")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientCompanyById");

        group.MapPut("/{id:guid}", RenameAsync)
            .WithName("RenameClientCompany")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Soft delete: the company is archived, not removed (see ClientCompany.Archive)
        group.MapDelete("/{id:guid}", ArchiveAsync)
            .WithName("ArchiveClientCompany");

        return app;
    }

    private static async Task<Ok<PagedResult<ClientCompanyResponse>>> ListAsync(
        [AsParameters] ListClientCompaniesRequest request, ListClientCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new ListClientCompaniesQuery(request.Page, request.PageSize, request.Search);

        return TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
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

    private static async Task<Results<Ok<ClientCompanyResponse>, NotFound>> RenameAsync(
        Guid id, RenameClientCompanyRequest request, RenameClientCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        var company = await handler.HandleAsync(new RenameClientCompanyCommand(id, request.Name), cancellationToken);

        return company is null ? TypedResults.NotFound() : TypedResults.Ok(company);
    }

    private static async Task<Results<NoContent, NotFound>> ArchiveAsync(
        Guid id, ArchiveClientCompanyHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
}
