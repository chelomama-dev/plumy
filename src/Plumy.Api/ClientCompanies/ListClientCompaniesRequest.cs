using System.ComponentModel.DataAnnotations;

namespace Plumy.Api.ClientCompanies;

/// <summary>
/// Query string of GET /api/client-companies. Validated by the .NET 10 minimal API validation (AddValidation).
/// </summary>
// Must be public: the validation source generator silently skips internal types (no error, just no validation)
public sealed record ListClientCompaniesRequest(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, ListClientCompaniesRequest.MaxPageSize)] int PageSize = ListClientCompaniesRequest.DefaultPageSize,
    [StringLength(ListClientCompaniesRequest.MaxSearchLength)] string? Search = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;
}
