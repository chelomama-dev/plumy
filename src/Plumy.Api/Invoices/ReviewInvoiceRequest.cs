namespace Plumy.Api.Invoices;

/// <summary>
/// Body of PUT /api/invoices/{id}/review.
/// </summary>
/// <param name="Status">Approved, Rejected, or Unreviewed to reopen it.</param>
/// <param name="RejectionReason">Required with Rejected.</param>
public sealed record ReviewInvoiceRequest(string? Status, string? RejectionReason);
