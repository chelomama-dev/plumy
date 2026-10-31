namespace Plumy.Domain.Billing;

/// <summary>
/// What a firm pays for. Each plan is a number of invoices a month: every invoice read costs us the reader and the
/// model, so the price follows the volume. The prices themselves live in Stripe, not here.
/// </summary>
public enum Plan
{
    /// <summary>The first <see cref="FirmSubscription.TrialDays"/> days of a new firm, before it pays anything.</summary>
    Trial,
    Basic,
    Pro
}

public static class Plans
{
    /// <summary>How many invoices the firm can upload in one period (the trial, or a billing month).</summary>
    public static int InvoiceLimit(this Plan plan) => plan switch
    {
        Plan.Trial => 50,
        Plan.Basic => 300,
        Plan.Pro => 1500,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null)
    };
}
