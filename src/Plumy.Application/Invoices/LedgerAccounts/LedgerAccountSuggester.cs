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

    /// <returns>Null when there is nothing to go on, or the classifier can't tell.</returns>
    public async Task<LedgerAccountSuggestion?> SuggestAsync(
        Invoice invoice, CancellationToken cancellationToken = default)
    {
        if (invoice.ExtractedData is not { } data)
            return null;

        if (data.SupplierTaxId is { } supplierTaxId)
        {
            var confirmed = await invoices.FindConfirmedLedgerAccountAsync(
                invoice.ClientCompanyId, supplierTaxId, cancellationToken);

            // An account that left the catalog is no longer a valid answer: let the classifier pick a current one
            if (ChartOfAccounts.Find(confirmed) is not null)
                return LedgerAccountSuggestion.Create(
                    confirmed!, LedgerAccountSuggestionSource.SupplierHistory, null, clock.GetUtcNow());
        }

        // Amounts alone say nothing about what was bought
        if (data.SupplierName is null && data.ItemsDescription is null)
            return null;

        var examples = await invoices.ListConfirmedLedgerAccountsAsync(
            invoice.ClientCompanyId, MaxExamples, cancellationToken);

        var classification = await classifier.ClassifyAsync(
            new LedgerAccountClassificationRequest(data, examples), cancellationToken);

        // The classifier is told to answer from the catalog; it is still checked, never trusted
        if (classification is null || ChartOfAccounts.Find(classification.Code) is null)
            return null;

        return LedgerAccountSuggestion.Create(
            classification.Code, LedgerAccountSuggestionSource.Ai, classification.Reason, clock.GetUtcNow());
    }
}
