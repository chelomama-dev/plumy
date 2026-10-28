using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ReviewInvoice;

/// <param name="Status">What the invoice should be now: Approved, Rejected, or Unreviewed to reopen it.</param>
/// <param name="RejectionReason">Required to reject; ignored otherwise.</param>
/// <param name="ExpectedVersion">The version the client loaded (If-Match), if known.</param>
public sealed record ReviewInvoiceCommand(
    Guid InvoiceId, InvoiceReviewStatus Status, string? RejectionReason, string? ExpectedVersion = null);

/// <summary>
/// A person approves, rejects or reopens an invoice. It sets the state asked for, wherever it starts from, so the
/// same request twice gives the same result, and going from rejected to approved needs no reopening in between.
/// </summary>
public sealed class ReviewInvoiceHandler(
    IInvoiceRepository invoices,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <returns>The invoice as it is now, or null if it doesn't exist (or is another firm's).</returns>
    public async Task<InvoiceResponse?> HandleAsync(
        ReviewInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return null;

        // Checked after: a retry of a request that already worked finds a newer version (its own), and must still
        // get the same answer, not a 412
        if (IsAlready(invoice, command))
            return await invoices.GetResponseByIdAsync(invoice.Id, cancellationToken);

        InvoiceVersion.EnsureMatches(invoice, command.ExpectedVersion);

        // The screen may be out of date (the worker hasn't finished): a conflict, not a bad request
        if (command.Status == InvoiceReviewStatus.Approved
            && invoice.ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new ConflictException("The invoice hasn't been read yet.");
        if (command.Status == InvoiceReviewStatus.Rejected
            && invoice.ExtractionStatus is InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Processing)
            throw new ConflictException("The invoice is still being read.");

        if (invoice.ReviewStatus != InvoiceReviewStatus.Unreviewed)
            invoice.Reopen();

        switch (command.Status)
        {
            case InvoiceReviewStatus.Approved:
                invoice.Approve(currentUser.ObjectId, clock.GetUtcNow());
                break;
            case InvoiceReviewStatus.Rejected:
                invoice.Reject(command.RejectionReason, currentUser.ObjectId, clock.GetUtcNow());
                break;
            case InvoiceReviewStatus.Unreviewed:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Status, "Unknown review status.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await invoices.GetResponseByIdAsync(invoice.Id, cancellationToken);
    }

    // Nothing to do. A rejection with another reason is a change: the reason is part of it
    private static bool IsAlready(Invoice invoice, ReviewInvoiceCommand command) =>
        invoice.ReviewStatus == command.Status
        && (command.Status != InvoiceReviewStatus.Rejected
            || invoice.RejectionReason == command.RejectionReason?.Trim());
}
