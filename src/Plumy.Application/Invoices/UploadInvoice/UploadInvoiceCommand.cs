namespace Plumy.Application.Invoices.UploadInvoice;

/// <param name="Content">The file. Must be seekable: it is read once to check and hash it, then again to store it.</param>
public sealed record UploadInvoiceCommand(Guid ClientCompanyId, string? FileName, Stream Content);
