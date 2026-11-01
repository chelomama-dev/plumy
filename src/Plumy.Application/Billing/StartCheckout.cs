using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Firms;
using Plumy.Domain.Billing;
using Plumy.Domain.Common;

namespace Plumy.Application.Billing;

/// <param name="Plan">"Basic" or "Pro".</param>
public sealed record StartCheckoutCommand(string Plan);

/// <summary>Where to send the browser next: a page hosted by Stripe.</summary>
public sealed record BillingRedirectResponse(Uri Url);

/// <summary>
/// The owner picks a plan and goes to Stripe to pay for it. Nothing changes in Plumy until Stripe's webhook says the
/// subscription exists (ADR 0012). Only owners get here: the API checks the role first.
/// </summary>
public sealed class StartCheckoutHandler(
    ITenantContext tenant, ICurrentUser user, IFirmRepository firms, IUnitOfWork unitOfWork, IBillingProvider billing)
{
    public async Task<BillingRedirectResponse> HandleAsync(
        StartCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<Plan>(command.Plan, ignoreCase: false, out var plan) || plan == Plan.Trial)
            throw new DomainException("Choose the Basic or the Pro plan.");

        var firm = await firms.GetAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        // A second Checkout would charge the firm twice. Changing plan is the portal's job
        if (firm.Subscription.IsPaying)
            throw new ConflictException("The firm already pays a plan. Change it from the billing portal.");

        // Saved before Checkout, so trying again reuses the customer instead of making another one
        if (firm.StripeCustomerId is null)
        {
            firm.LinkStripeCustomer(
                await billing.CreateCustomerAsync(firm.Id, firm.Name, user.Email, cancellationToken));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new BillingRedirectResponse(
            await billing.CreateCheckoutAsync(firm.StripeCustomerId!, firm.Id, plan, cancellationToken));
    }
}
