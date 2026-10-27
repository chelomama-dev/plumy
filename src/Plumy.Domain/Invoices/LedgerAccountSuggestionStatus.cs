namespace Plumy.Domain.Invoices;

/// <summary>Where the account suggestion of a read invoice stands. None until the invoice is read.</summary>
public enum LedgerAccountSuggestionStatus
{
    /// <summary>Read; the worker is about to suggest an account (a second or two).</summary>
    Pending,

    /// <summary>An account is suggested: <see cref="Invoice.LedgerAccountSuggestion"/>.</summary>
    Suggested,

    /// <summary>
    /// Nothing to suggest on purpose: too little was read, or the AI couldn't tell (no suggestion is better than a
    /// wrong one). <see cref="Invoice.NoSuggestionReason"/> says why, when known.
    /// </summary>
    NoSuggestion,

    /// <summary>The suggestion kept failing (the AI service was down): a person picks the account.</summary>
    Failed
}
