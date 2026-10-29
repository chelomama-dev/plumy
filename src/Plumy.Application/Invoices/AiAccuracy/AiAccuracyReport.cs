using System.Text.Json;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.AiAccuracy;

/// <param name="ClientCompanyId">One company, or null for every company of the firm.</param>
/// <param name="From">Approved on or after this day (UTC), inclusive.</param>
/// <param name="To">Approved on or before this day (UTC), inclusive.</param>
public sealed record GetAiAccuracyQuery(Guid? ClientCompanyId, DateOnly? From, DateOnly? To);

/// <summary>
/// What the AI proposed for an approved invoice next to what a person left in the end: the suggested account against
/// the confirmed one, and the reader's confidence per field against the fields a person corrected.
/// </summary>
/// <param name="Suggestion">Only when the invoice got one (<see cref="LedgerAccountSuggestionStatus.Suggested"/>).</param>
public sealed record AiAccuracySample(
    LedgerAccountSuggestion? Suggestion,
    string ConfirmedLedgerAccountCode,
    IReadOnlyDictionary<InvoiceField, double> FieldConfidence,
    IReadOnlyList<InvoiceField> CorrectedFields)
{
    /// <summary>From an approved invoice, which always has a confirmed account.</summary>
    public static AiAccuracySample From(Invoice invoice) => new(
        invoice.SuggestionStatus == LedgerAccountSuggestionStatus.Suggested ? invoice.LedgerAccountSuggestion : null,
        invoice.LedgerAccountCode ?? throw new InvalidOperationException("An approved invoice has a confirmed account."),
        invoice.FieldConfidence, invoice.CorrectedFields);
}

/// <summary>
/// How often a person kept what the AI proposed, over approved invoices: an approved invoice is one a person checked
/// field by field, so what it ended up with is the right answer to compare against.
/// </summary>
/// <param name="ApprovedInvoices">How many invoices the report is about.</param>
public sealed record AiAccuracyReport(
    int ApprovedInvoices,
    LedgerAccountAccuracy LedgerAccounts,
    IReadOnlyList<FieldAccuracy> Fields,
    IReadOnlyList<ConfidenceBandAccuracy> ConfidenceBands)
{
    /// <summary>
    /// The edges of the confidence bands. 0.8 is where the review screen starts calling a field doubtful
    /// (lowConfidenceThreshold in the web app): the bands around it say whether that line is in the right place.
    /// </summary>
    public static readonly IReadOnlyList<double> BandEdges = [0, 0.5, 0.8, 0.9, 0.95, 1];

    public static AiAccuracyReport Calculate(IReadOnlyCollection<AiAccuracySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        return new AiAccuracyReport(
            samples.Count, LedgerAccountAccuracy.Calculate(samples), FieldAccuracy.Calculate(samples),
            ConfidenceBandAccuracy.Calculate(samples));
    }

    // Same names as the invoice's JSON (fieldConfidence, correctedFields), so the client reuses its labels
    internal static string JsonName(InvoiceField field) => JsonNamingPolicy.CamelCase.ConvertName(field.ToString());
}

/// <param name="Ai">The language model's suggestions: suppliers the company had no confirmed account for.</param>
/// <param name="SupplierHistory">Repeating the account last confirmed for the same supplier. No AI involved.</param>
/// <param name="NotSuggested">Approved with nothing suggested: the AI declined, failed, or a person was faster.</param>
public sealed record LedgerAccountAccuracy(
    SuggestionAccuracy Ai, SuggestionAccuracy SupplierHistory, int NotSuggested)
{
    internal static LedgerAccountAccuracy Calculate(IReadOnlyCollection<AiAccuracySample> samples)
    {
        SuggestionAccuracy BySource(LedgerAccountSuggestionSource source)
        {
            var suggested = samples.Where(s => s.Suggestion?.Source == source).ToList();
            return new SuggestionAccuracy(
                suggested.Count, suggested.Count(s => s.Suggestion!.Code == s.ConfirmedLedgerAccountCode));
        }

        return new LedgerAccountAccuracy(
            BySource(LedgerAccountSuggestionSource.Ai), BySource(LedgerAccountSuggestionSource.SupplierHistory),
            samples.Count(s => s.Suggestion is null));
    }
}

/// <param name="Accepted">The confirmed account is the suggested one.</param>
public sealed record SuggestionAccuracy(int Suggested, int Accepted);

/// <summary>How one field of the invoice was read, over the invoices the reader gave confidences for.</summary>
/// <param name="Field">The field's JSON name (camelCase), as in the invoice.</param>
/// <param name="Read">The reader returned a value for it.</param>
/// <param name="Corrected">Of those, how many a person changed.</param>
/// <param name="Missed">The reader returned nothing and a person filled it in.</param>
public sealed record FieldAccuracy(string Field, int Read, int Corrected, int Missed)
{
    internal static IReadOnlyList<FieldAccuracy> Calculate(IReadOnlyCollection<AiAccuracySample> samples)
    {
        // A reader that gives no confidence at all says nothing about which fields it found
        var withConfidence = samples.Where(s => s.FieldConfidence.Count > 0).ToList();

        return Enum.GetValues<InvoiceField>()
            .Select(field => new FieldAccuracy(
                AiAccuracyReport.JsonName(field),
                withConfidence.Count(s => s.FieldConfidence.ContainsKey(field)),
                withConfidence.Count(s => s.FieldConfidence.ContainsKey(field) && s.CorrectedFields.Contains(field)),
                withConfidence.Count(s => !s.FieldConfidence.ContainsKey(field) && s.CorrectedFields.Contains(field))))
            .ToList();
    }
}

/// <summary>Fields read with a confidence in [From, To) (the last band includes 1), and how many were corrected.</summary>
public sealed record ConfidenceBandAccuracy(double From, double To, int Read, int Corrected)
{
    internal static IReadOnlyList<ConfidenceBandAccuracy> Calculate(IReadOnlyCollection<AiAccuracySample> samples)
    {
        var fields = samples
            .SelectMany(s => s.FieldConfidence.Select(pair => (Confidence: pair.Value,
                Corrected: s.CorrectedFields.Contains(pair.Key))))
            .ToList();

        var edges = AiAccuracyReport.BandEdges;
        return edges.Zip(edges.Skip(1), (from, to) =>
            {
                var last = to == edges[^1];
                var inBand = fields.Where(f => f.Confidence >= from && (f.Confidence < to || (last && f.Confidence <= to)))
                    .ToList();
                return new ConfidenceBandAccuracy(from, to, inBand.Count, inBand.Count(f => f.Corrected));
            })
            .ToList();
    }
}
