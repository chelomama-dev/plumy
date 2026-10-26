using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Infrastructure;
using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.ConfirmLedgerAccount;
using Plumy.Application.Invoices.CorrectInvoiceData;
using Plumy.Application.Invoices.Export;
using Plumy.Application.Invoices.GetInvoiceById;
using Plumy.Application.Invoices.GetInvoiceFile;
using Plumy.Application.Invoices.ListInvoices;
using Plumy.Application.Invoices.RetryExtraction;
using Plumy.Application.Invoices.ReviewInvoice;
using Plumy.Application.Invoices.UploadInvoice;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Invoices;

internal static class InvoiceEndpoints
{
    private const string BasePath = "/api/invoices";

    // The file plus the multipart envelope. A bigger file still fits, so it gets the domain's clear 400 instead of
    // a bare 413; anything far bigger is cut off before it is read
    private const long MaxRequestBytes = Invoice.MaxFileSizeBytes + 1024 * 1024;

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        // Uploads hang from the company they belong to: an unknown or foreign company is a plain 404
        app.MapPost("/api/client-companies/{clientCompanyId:guid}/invoices", UploadAsync)
            .WithTags("Invoices")
            .WithName("UploadInvoice")
            .RequireFirmMembership()
            // Antiforgery protects cookie-authenticated forms. This API only accepts bearer tokens, which a
            // cross-site form can't attach, so there is nothing to forge
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes)
            .Accepts<IFormFile>("multipart/form-data")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var group = app.MapGroup(BasePath)
            .WithTags("Invoices")
            .RequireFirmMembership();

        group.MapGet("/", ListAsync)
            .WithName("ListInvoices")
            .ProducesValidationProblem();

        // Any member: exporting only reads. A GET, so the browser (or a script) can ask for it again
        group.MapGet("/export", ExportAsync)
            .WithName("ExportInvoices")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetInvoiceById");

        group.MapGet("/{id:guid}/file", GetFileAsync)
            .WithName("GetInvoiceFile");

        // Any member, like the upload: whoever can add an invoice can ask for it to be read again.
        // A POST to the extraction "sub-resource": it starts a new one, it doesn't edit the invoice
        group.MapPost("/{id:guid}/extraction", RetryExtractionAsync)
            .WithName("RetryInvoiceExtraction")
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Any member: whoever reviews invoices sets their account. PUT: the same request twice leaves the same result
        group.MapPut("/{id:guid}/ledger-account", ConfirmLedgerAccountAsync)
            .WithName("ConfirmLedgerAccount")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Any member, like the account. The whole data each time: the screen sends the form as it is
        group.MapPut("/{id:guid}/data", CorrectDataAsync)
            .WithName("CorrectInvoiceData")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Any member: approving is the reviewer's job, not only the owner's. PUT of the state wanted, so a retried
        // request is harmless
        group.MapPut("/{id:guid}/review", ReviewAsync)
            .WithName("ReviewInvoice")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Created<InvoiceResponse>, NotFound>> UploadAsync(
        Guid clientCompanyId, IFormFile file, UploadInvoiceHandler handler, CancellationToken cancellationToken)
    {
        // Buffered by the form reader (memory, then a temp file), so it can be read more than once
        await using var content = file.OpenReadStream();

        var invoice = await handler.HandleAsync(
            new UploadInvoiceCommand(clientCompanyId, file.FileName, content), cancellationToken);

        return invoice is null
            ? TypedResults.NotFound()
            : TypedResults.Created($"{BasePath}/{invoice.Id}", invoice);
    }

    private static async Task<Ok<PagedResult<InvoiceResponse>>> ListAsync(
        [AsParameters] ListInvoicesRequest request, ListInvoicesHandler handler, CancellationToken cancellationToken)
    {
        var query = new ListInvoicesQuery(request.Page, request.PageSize, request.ClientCompanyId);

        return TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> ExportAsync(
        [AsParameters] ExportInvoicesRequest request, ExportInvoicesHandler handler, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var format = request.Format == "csv" ? InvoiceExportFormat.Csv : InvoiceExportFormat.Xlsx;
        var file = await handler.HandleAsync(
            new ExportInvoicesQuery(request.ClientCompanyId, request.From, request.To, format), cancellationToken);
        if (file is null)
            return TypedResults.NotFound();

        // The books of a company: no proxy or browser cache should keep a copy
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";

        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }

    private static async Task<Results<Ok<InvoiceResponse>, NotFound>> GetByIdAsync(
        Guid id, GetInvoiceByIdHandler handler, CancellationToken cancellationToken)
    {
        var invoice = await handler.HandleAsync(id, cancellationToken);

        return invoice is null ? TypedResults.NotFound() : TypedResults.Ok(invoice);
    }

    // 202: the request is accepted, the reading itself happens later in the worker
    private static async Task<Results<Accepted<InvoiceResponse>, NotFound>> RetryExtractionAsync(
        Guid id, RetryInvoiceExtractionHandler handler, CancellationToken cancellationToken)
    {
        var invoice = await handler.HandleAsync(id, cancellationToken);

        return invoice is null ? TypedResults.NotFound() : TypedResults.Accepted($"{BasePath}/{id}", invoice);
    }

    private static async Task<Results<Ok<InvoiceResponse>, NotFound>> ConfirmLedgerAccountAsync(
        Guid id, ConfirmLedgerAccountRequest request, ConfirmLedgerAccountHandler handler,
        CancellationToken cancellationToken)
    {
        var invoice = await handler.HandleAsync(new ConfirmLedgerAccountCommand(id, request.Code), cancellationToken);

        return invoice is null ? TypedResults.NotFound() : TypedResults.Ok(invoice);
    }

    private static async Task<Results<Ok<InvoiceResponse>, NotFound>> CorrectDataAsync(
        Guid id, CorrectInvoiceDataRequest request, CorrectInvoiceDataHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CorrectInvoiceDataCommand(
            id, request.SupplierName, request.SupplierTaxId, request.CustomerTaxId, request.InvoiceNumber,
            request.IssueDate, request.NetAmount, request.TaxAmount, request.TotalAmount, request.Currency,
            request.ItemsDescription);

        var invoice = await handler.HandleAsync(command, cancellationToken);

        return invoice is null ? TypedResults.NotFound() : TypedResults.Ok(invoice);
    }

    private static async Task<Results<Ok<InvoiceResponse>, NotFound, ValidationProblem>> ReviewAsync(
        Guid id, ReviewInvoiceRequest request, ReviewInvoiceHandler handler, CancellationToken cancellationToken)
    {
        // By name only: Enum.TryParse also takes "1" or "42", which are no status
        if (!Enum.TryParse<InvoiceReviewStatus>(request.Status, ignoreCase: true, out var status)
            || !Enum.GetNames<InvoiceReviewStatus>().Contains(request.Status, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<InvoiceReviewStatus>())}."]
            });
        }

        var invoice = await handler.HandleAsync(
            new ReviewInvoiceCommand(id, status, request.RejectionReason), cancellationToken);

        return invoice is null ? TypedResults.NotFound() : TypedResults.Ok(invoice);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetFileAsync(
        Guid id, GetInvoiceFileHandler handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var file = await handler.HandleAsync(id, cancellationToken);
        if (file is null)
            return TypedResults.NotFound();

        // The format was checked on upload, so the browser has no reason to guess another one
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";

        // Attachment with the original name (RFC 6266, UTF-8 safe). The result disposes the stream
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }
}
