namespace Plumy.Api.Invoices;

/// <summary>Query string of GET /api/invoices/ai-accuracy. Every filter is optional.</summary>
/// <param name="From">Approved on or after this day (UTC).</param>
/// <param name="To">Approved on or before this day (UTC).</param>
public sealed record GetAiAccuracyRequest(Guid? ClientCompanyId = null, DateOnly? From = null, DateOnly? To = null);
