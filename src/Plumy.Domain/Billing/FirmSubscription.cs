namespace Plumy.Domain.Billing;

/// <summary>
/// The firm's plan and the period its invoices are counted in. A new firm starts a trial; Stripe takes over when
/// the owner picks a plan (ADR 0012). Only uploading depends on it: reading, reviewing and exporting the invoices
/// already in Plumy never stop, because they are the firm's books.
/// </summary>
public sealed record FirmSubscription
{
    public const int TrialDays = 14;

    public Plan Plan { get; private init; }
    public SubscriptionStatus Status { get; private init; }

    /// <summary>Invoices uploaded from here on count towards the plan's limit.</summary>
    public DateTimeOffset PeriodStart { get; private init; }

    /// <summary>
    /// When the trial ends, or when Stripe bills the next month. A paid period that has passed with no news from
    /// Stripe still works: the renewal is on its way and moves the period forward.
    /// </summary>
    public DateTimeOffset PeriodEnd { get; private init; }

    private FirmSubscription(Plan plan, SubscriptionStatus status, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        Plan = plan;
        Status = status;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    public static FirmSubscription StartTrial(DateTimeOffset now) =>
        new(Plan.Trial, SubscriptionStatus.Trialing, now, now.AddDays(TrialDays));

    public int InvoiceLimit => Plan.InvoiceLimit();

    /// <summary>
    /// Whether one more invoice can be uploaded, given how many the firm has uploaded since <see cref="PeriodStart"/>.
    /// </summary>
    public UploadBlock CheckUpload(DateTimeOffset now, int uploadedInPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uploadedInPeriod);

        return Status switch
        {
            SubscriptionStatus.Canceled => UploadBlock.NotSubscribed,
            SubscriptionStatus.Trialing when now >= PeriodEnd => UploadBlock.TrialEnded,
            _ when uploadedInPeriod >= InvoiceLimit => UploadBlock.LimitReached,
            _ => UploadBlock.None
        };
    }
}
