using Plumy.Domain.Common;

namespace Plumy.Application.Invoices.AiAccuracy;

/// <summary>
/// How well the AI is doing for the firm (or one company): how often its account suggestions are kept, and which
/// fields the reader gets wrong. Computed on the fly from the approved invoices: there is nothing to keep in sync.
/// </summary>
public sealed class GetAiAccuracyHandler(IInvoiceRepository invoices)
{
    /// <summary>
    /// The newest approved invoices it looks at, at most. Each one is a handful of bytes, so this is far from a
    /// memory problem; it just keeps one request bounded. Years of a small firm fit.
    /// </summary>
    public const int MaxInvoices = 50_000;

    /// <returns>Null when the company doesn't exist (or is another firm's).</returns>
    public async Task<AiAccuracyReport?> HandleAsync(GetAiAccuracyQuery query, CancellationToken cancellationToken = default)
    {
        if (query is { From: { } from, To: { } to } && from > to)
            throw new DomainException("The start date must not be after the end date.");

        // Archived companies too, as in the export: their invoices still tell how the AI did
        if (query.ClientCompanyId is { } companyId
            && await invoices.GetClientCompanyTaxIdAsync(companyId, cancellationToken) is null)
            return null;

        var samples = await invoices.ListAiAccuracySamplesAsync(
            query.ClientCompanyId, query.From, query.To, MaxInvoices, cancellationToken);

        return AiAccuracyReport.Calculate(samples);
    }
}
