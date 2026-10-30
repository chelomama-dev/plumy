using Plumy.Application.Abstractions;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Firms.AutoApproval;

/// <param name="MinConfidence">From 0.8 to 1: how sure the reader must be of every booked field.</param>
public sealed record AutoApprovalResponse(bool Enabled, double MinConfidence)
{
    public static AutoApprovalResponse From(AutoApprovalPolicy policy) => new(policy.Enabled, policy.MinConfidence);
}

public sealed record SetAutoApprovalCommand(bool Enabled, double MinConfidence);

/// <summary>Whether the current firm lets Plumy approve invoices by itself. Any member can see it.</summary>
public sealed class GetAutoApprovalHandler(ITenantContext tenant, IFirmRepository repository)
{
    public async Task<AutoApprovalResponse> HandleAsync(CancellationToken cancellationToken = default) =>
        AutoApprovalResponse.From(await repository.GetAutoApprovalAsync(tenant.FirmId, cancellationToken));
}

/// <summary>
/// The owner turns auto-approval on or off, or moves its line. Applies to the invoices read from then on: the ones
/// already waiting for a person stay as they are. Only owners get here: the API checks the role first.
/// </summary>
public sealed class SetAutoApprovalHandler(ITenantContext tenant, IFirmRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AutoApprovalResponse> HandleAsync(
        SetAutoApprovalCommand command, CancellationToken cancellationToken = default)
    {
        var firm = await repository.GetAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        firm.SetAutoApproval(AutoApprovalPolicy.Create(command.Enabled, command.MinConfidence));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AutoApprovalResponse.From(firm.AutoApproval);
    }
}
