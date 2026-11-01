using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Firms;

namespace Plumy.Application.Billing;

/// <summary>
/// The owner goes to Stripe's portal to change plan, update the card, download receipts or cancel. Only owners get
/// here: the API checks the role first.
/// </summary>
public sealed class OpenBillingPortalHandler(ITenantContext tenant, IFirmRepository firms, IBillingProvider billing)
{
    public async Task<BillingRedirectResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        var firm = await firms.GetAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        if (firm.StripeCustomerId is null)
            throw new ConflictException("The firm hasn't paid for a plan yet. Choose one first.");

        return new BillingRedirectResponse(await billing.CreatePortalAsync(firm.StripeCustomerId, cancellationToken));
    }
}
