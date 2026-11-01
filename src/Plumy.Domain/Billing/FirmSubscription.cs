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

    /// <summary>Stripe's id of the paid subscription (sub_...). Null during the trial.</summary>
    public string? StripeSubscriptionId { get; private init; }

    private FirmSubscription(
        Plan plan, SubscriptionStatus status, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string? stripeSubscriptionId = null)
    {
        Plan = plan;
        Status = status;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        StripeSubscriptionId = stripeSubscriptionId;
    }

    public static FirmSubscription StartTrial(DateTimeOffset now) =>
        new(Plan.Trial, SubscriptionStatus.Trialing, now, now.AddDays(TrialDays));

    /// <summary>
    /// A subscription the firm pays in Stripe, as Stripe describes it now: the plan, where its payments are and the
    /// billing month whose uploads count.
    /// </summary>
    public static FirmSubscription Paid(
        string stripeSubscriptionId, Plan plan, SubscriptionStatus status, DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeSubscriptionId);
        if (plan == Plan.Trial)
            throw new ArgumentOutOfRangeException(nameof(plan), plan, "The trial is not paid for.");
        if (status == SubscriptionStatus.Trialing)
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only Plumy runs trials, not Stripe.");
        if (periodEnd <= periodStart)
            throw new ArgumentException("The period must end after it starts.", nameof(periodEnd));

        return new FirmSubscription(plan, status, periodStart, periodEnd, stripeSubscriptionId);
    }

    /// <summary>Paid, and not canceled: the firm shouldn't start a second subscription next to this one.</summary>
    public bool IsPaying => StripeSubscriptionId is not null && Status != SubscriptionStatus.Canceled;

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
