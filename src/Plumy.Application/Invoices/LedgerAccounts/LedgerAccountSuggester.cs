using Plumy.Domain.Accounting;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.LedgerAccounts;

/// <summary>
/// Proposes the account for a newly read invoice. Rules first, AI only for what rules can't answer: a supplier the
/// company already has a confirmed account for gets that account again, with no model involved (free, instant and
/// what the accountant expects). Only a new supplier goes to the classifier.
/// </summary>
public sealed class LedgerAccountSuggester(
    IInvoiceRepository invoices,
    ILedgerAccountClassifier classifier,
    TimeProvider clock)
{
    /// <summary>Enough for the model to see the firm's habits, small enough to keep each call cheap.</summary>
    public const int MaxExamples = 20;

    /// <summary>For the reviewer, like the AI's own reasons (which come in Spanish).</summary>
    public const string NothingReadReason =
        "No se ha leído ni el proveedor ni los conceptos: no hay con qué elegir la cuenta.";

    /// <returns>The suggestion, or why there is none.</returns>
    public async Task<LedgerAccountSuggestionResult> SuggestAsync(
        Invoice invoice, CancellationToken cancellationToken = default)
    {
        if (invoice.ExtractedData is not { } data)
            return LedgerAccountSuggestionResult.None(NothingReadReason);

        if (data.SupplierTaxId is { } supplierTaxId)
        {
            var confirmed = await invoices.FindConfirmedLedgerAccountAsync(
                invoice.ClientCompanyId, supplierTaxId, cancellationToken);

            // An account that left the catalog is no longer a valid answer: let the classifier pick a current one
            if (ChartOfAccounts.Find(confirmed) is not null)
                return LedgerAccountSuggestionResult.Of(LedgerAccountSuggestion.Create(
                    confirmed!, LedgerAccountSuggestionSource.SupplierHistory, null, clock.GetUtcNow()));
        }

        // Amounts alone say nothing about what was bought
        if (data.SupplierName is null && data.ItemsDescription is null)
            return LedgerAccountSuggestionResult.None(NothingReadReason);

        var examples = await invoices.ListConfirmedLedgerAccountsAsync(
            invoice.ClientCompanyId, MaxExamples, cancellationToken);

        var classification = await classifier.ClassifyAsync(
            new LedgerAccountClassificationRequest(data, examples), cancellationToken);

        // No usable answer: nothing to tell the reviewer beyond "no suggestion"
        if (classification is null)
            return LedgerAccountSuggestionResult.None(null);

        // It couldn't tell: its reason is what helps the reviewer most
        if (classification.Code is null)
            return LedgerAccountSuggestionResult.None(classification.Reason);

        // The classifier is told to answer from the catalog; it is still checked, never trusted
        if (ChartOfAccounts.Find(classification.Code) is null)
            return LedgerAccountSuggestionResult.None(null);

        return LedgerAccountSuggestionResult.Of(LedgerAccountSuggestion.Create(
            classification.Code, LedgerAccountSuggestionSource.Ai, classification.Reason, clock.GetUtcNow()));
    }
}

/// <summary>An account to suggest, or none and (when known) why.</summary>
public sealed record LedgerAccountSuggestionResult(LedgerAccountSuggestion? Suggestion, string? NoSuggestionReason)
{
    public static LedgerAccountSuggestionResult Of(LedgerAccountSuggestion suggestion) => new(suggestion, null);

    public static LedgerAccountSuggestionResult None(string? reason) => new(null, reason);
}
