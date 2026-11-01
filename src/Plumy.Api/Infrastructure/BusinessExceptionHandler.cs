using Microsoft.AspNetCore.Diagnostics;
using Plumy.Application.Billing;
using Plumy.Application.Common;
using Plumy.Domain.Common;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// Turns expected business errors into RFC 9457 ProblemDetails. Anything else falls through to a generic 500.
/// </summary>
internal sealed class BusinessExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DomainException => (StatusCodes.Status400BadRequest, "Validation error"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            PaymentRequiredException => (StatusCodes.Status402PaymentRequired, "Payment required"),
            PreconditionFailedException => (StatusCodes.Status412PreconditionFailed, "Precondition failed"),
            InvalidBillingEventException => (StatusCodes.Status400BadRequest, "Invalid webhook"),
            BillingUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Payments unavailable"),
            // Malformed or missing body; only thrown in Development, where minimal APIs surface binding errors
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request"),
            _ => (0, null)
        };

        if (status == 0)
            return false;

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                // Safe to expose: business messages are written for the client, unlike unexpected exceptions
                Detail = exception.Message
            }
        });
    }
}
