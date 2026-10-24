using System.ComponentModel.DataAnnotations;

namespace Plumy.Api.Invoices;

/// <summary>Query string of GET /api/invoices/export. Public so the validation source generator sees it.</summary>
/// <param name="From">yyyy-MM-dd, inclusive.</param>
/// <param name="To">yyyy-MM-dd, inclusive.</param>
public sealed record ExportInvoicesRequest(
    Guid? ClientCompanyId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    [AllowedValues("xlsx", "csv")] string Format = "xlsx");
