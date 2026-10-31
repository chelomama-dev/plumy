namespace Plumy.Domain.Billing;

/// <summary>Where the firm is with its payments. Stripe's statuses map onto these few; see ADR 0012.</summary>
public enum SubscriptionStatus
{
    /// <summary>The free trial, run by Plumy: no card, no Stripe.</summary>
    Trialing,

    /// <summary>Paid and up to date.</summary>
    Active,

    /// <summary>A payment failed and Stripe is retrying it. The firm keeps working meanwhile.</summary>
    PastDue,

    /// <summary>No longer paying. The firm keeps its data but can't upload more.</summary>
    Canceled
}
