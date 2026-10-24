using Plumy.Application.Invoices.LedgerAccounts;

namespace Plumy.Infrastructure.Classification;

/// <summary>
/// Always suggests 629 Otros servicios. For local development and tests: no Azure account, nothing paid, and the
/// whole flow (suggest, confirm, learn from the history) still works.
/// </summary>
internal sealed class FakeLedgerAccountClassifier : ILedgerAccountClassifier
{
    internal const string Code = "629";
    internal const string Reason = "Sugerencia de ejemplo (clasificador de desarrollo).";

    public Task<LedgerAccountClassification?> ClassifyAsync(
        LedgerAccountClassificationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<LedgerAccountClassification?>(new LedgerAccountClassification(Code, Reason));
}
