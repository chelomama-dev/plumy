using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ConfirmLedgerAccount;

/// <param name="Code">A code of the chart of accounts: the suggested one or any other.</param>
/// <param name="ExpectedVersion">The version the client loaded (If-Match), if known.</param>
public sealed record ConfirmLedgerAccountCommand(Guid InvoiceId, string? Code, string? ExpectedVersion = null);

/// <summary>
/// A person sets the invoice's account. This is what the suggestions learn from: the next invoice of the same
/// supplier gets this account, and the AI sees it among the company's examples.
/// </summary>
public sealed class ConfirmLedgerAccountHandler(
    IInvoiceRepository invoices,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <returns>The invoice as it is now, or null if it doesn't exist (or is another firm's).</returns>
    public async Task<InvoiceResponse?> HandleAsync(
        ConfirmLedgerAccountCommand command, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return null;

        InvoiceVersion.EnsureMatches(invoice, command.ExpectedVersion);

        // The list may be out of date (the worker hasn't finished): a conflict, not a bad request
        if (invoice.ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new ConflictException("The invoice hasn't been read yet.");
        if (invoice.ReviewStatus != InvoiceReviewStatus.Unreviewed)
            throw new ConflictException($"The invoice is {invoice.ReviewStatus}: reopen it to change it.");

        invoice.ConfirmLedgerAccount(command.Code, currentUser.ObjectId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await invoices.GetResponseByIdAsync(invoice.Id, cancellationToken);
    }
}
