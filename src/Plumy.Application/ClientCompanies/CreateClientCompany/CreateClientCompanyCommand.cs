namespace Plumy.Application.ClientCompanies.CreateClientCompany;

// Nullable on purpose: the domain is the one that validates, not the JSON deserializer
public sealed record CreateClientCompanyCommand(string? Name, string? TaxId);
