namespace Plumy.Api.ClientCompanies;

/// <summary>
/// Body of PUT /api/client-companies/{id}. The Id comes from the route, so the HTTP contract and the
/// application command are different shapes. Only the name is editable: the tax ID identifies the company.
/// </summary>
public sealed record RenameClientCompanyRequest(string? Name);
