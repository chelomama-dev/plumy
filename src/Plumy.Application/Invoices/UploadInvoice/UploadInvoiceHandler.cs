using System.Security.Cryptography;
using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.UploadInvoice;

public sealed class UploadInvoiceHandler(
    IClientCompanyRepository companies,
    IInvoiceRepository invoices,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <returns>The new invoice, or null if the client company doesn't exist (or is archived, or another firm's).</returns>
    public async Task<InvoiceResponse?> HandleAsync(
        UploadInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var content = command.Content;
        if (!content.CanSeek)
            throw new ArgumentException("The file stream must be seekable.", nameof(command));

        // Tenant filter + archived filter: another firm's company looks exactly like one that doesn't exist
        var company = await companies.GetByIdAsync(command.ClientCompanyId, cancellationToken);
        if (company is null)
            return null;

        Invoice.EnsureValidSize(content.Length);

        var format = await DetectFormatAsync(content, cancellationToken)
            ?? throw new DomainException("Only PDF, JPEG and PNG files are accepted.");

        content.Position = 0;
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));

        var invoice = Invoice.Create(
            tenant.FirmId, company.Id, command.FileName, format, content.Length, sha256, currentUser.ObjectId,
            clock.GetUtcNow());

        // The same file twice is almost always a mistake (and would be booked twice). Fast path with a clear
        // message; the unique index is the real guarantee when two uploads race
        if (await invoices.ExistsAsync(company.Id, sha256, cancellationToken))
            throw new ConflictException($"This file has already been uploaded for '{company.Name}'.");

        // File first, then the row: a failure in between leaves at most an orphan file, never a row whose file
        // doesn't exist
        content.Position = 0;
        await storage.UploadAsync(invoice.StoragePath, content, format.ContentType(), cancellationToken);

        invoices.Add(invoice);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Not the request's token: if the client cancelled, the clean-up must still happen
            await storage.DeleteIfExistsAsync(invoice.StoragePath, CancellationToken.None);
            throw;
        }

        return InvoiceResponse.From(invoice, company.Name);
    }

    private static async Task<InvoiceFileFormat?> DetectFormatAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        var header = new byte[InvoiceFileFormats.SignatureLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        return InvoiceFileFormats.Detect(header.AsSpan(0, read));
    }
}
