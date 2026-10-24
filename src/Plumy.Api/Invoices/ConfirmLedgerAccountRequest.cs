namespace Plumy.Api.Invoices;

/// <summary>Body of PUT /api/invoices/{id}/ledger-account: the account a person chose.</summary>
public sealed record ConfirmLedgerAccountRequest(string? Code);
