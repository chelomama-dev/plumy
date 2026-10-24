using Plumy.Domain.Accounting;
using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>
/// The account Plumy proposes for an invoice, before a person confirms it. Kept after the confirmation too: comparing
/// both says how often the suggestions are right.
/// </summary>
public sealed record LedgerAccountSuggestion
{
    public const int MaxReasonLength = 300;

    public string Code { get; private init; }
    public LedgerAccountSuggestionSource Source { get; private init; }

    /// <summary>Why, in a sentence for the reviewer. Only the AI gives one: the history speaks for itself.</summary>
    public string? Reason { get; private init; }

    public DateTimeOffset SuggestedAt { get; private init; }

    // For EF Core
    private LedgerAccountSuggestion()
    {
        Code = "";
    }

    public static LedgerAccountSuggestion Create(
        string code, LedgerAccountSuggestionSource source, string? reason, DateTimeOffset suggestedAt)
    {
        if (ChartOfAccounts.Find(code) is null)
            throw new DomainException($"Unknown ledger account '{code}'.");
        if (!Enum.IsDefined(source))
            throw new DomainException("Unknown suggestion source.");

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            trimmed = null;
        else if (trimmed.Length > MaxReasonLength)
            trimmed = trimmed[..MaxReasonLength].TrimEnd();

        return new LedgerAccountSuggestion
        {
            Code = code,
            Source = source,
            Reason = trimmed,
            SuggestedAt = suggestedAt
        };
    }
}

public enum LedgerAccountSuggestionSource
{
    /// <summary>The account a person last confirmed for the same supplier in the same company. No AI involved.</summary>
    SupplierHistory,

    /// <summary>A language model, for a supplier the company has no confirmed history with.</summary>
    Ai
}
