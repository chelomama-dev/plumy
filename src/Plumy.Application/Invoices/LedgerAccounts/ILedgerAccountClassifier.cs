using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.LedgerAccounts;

/// <summary>
/// Picks the ledger account for an invoice the company has no history with (a language model in Microsoft Foundry;
/// a fake one locally and in tests).
/// </summary>
public interface ILedgerAccountClassifier
{
    /// <summary>
    /// A classification with a null code when it can't tell (no suggestion is better than a wrong one), with its
    /// reason. Null when it gave no usable answer at all (refused, cut short, outside the contract). Any exception
    /// is taken as transient (the service is down, throttling) and the worker's message is retried.
    /// </summary>
    Task<LedgerAccountClassification?> ClassifyAsync(
        LedgerAccountClassificationRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Invoice">What was read from it.</param>
/// <param name="Examples">
/// The company's own past choices, so the model follows how this firm books things (one firm sends software to 629,
/// another to 206). Only from the same company: never another company's or firm's data.
/// </param>
public sealed record LedgerAccountClassificationRequest(
    ExtractedInvoiceData Invoice,
    IReadOnlyList<ConfirmedLedgerAccountExample> Examples);

/// <summary>An invoice of the same company whose account a person confirmed.</summary>
public sealed record ConfirmedLedgerAccountExample(
    string? SupplierName,
    string? ItemsDescription,
    string LedgerAccountCode);

/// <param name="Code">A code from <c>ChartOfAccounts</c>, or null: the classifier couldn't tell.</param>
/// <param name="Reason">One sentence for the reviewer: why this account, or why none.</param>
public sealed record LedgerAccountClassification(string? Code, string? Reason);
