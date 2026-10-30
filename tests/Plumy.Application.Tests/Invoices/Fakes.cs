using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.AiAccuracy;
using Plumy.Application.Invoices.Export;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

/// <summary>Same idea as InMemoryClientCompanyStore: Add stages, SaveChangesAsync makes it visible.</summary>
internal sealed class InMemoryInvoiceStore : IInvoiceRepository, IUnitOfWork
{
    private readonly List<Invoice> _pending = [];

    public List<Invoice> Saved { get; } = [];

    /// <summary>Set to make the next save fail, like a database error after the file was stored.</summary>
    public Exception? FailNextSave { get; set; }

    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(i => i.Id == id));

    /// <summary>Counts every save, including the ones that change an invoice already stored.</summary>
    public int SaveCount { get; private set; }

    /// <summary>The join with the company is a SQL concern (covered against SQL Server); here the name is fixed.</summary>
    public Task<InvoiceResponse?> GetResponseByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.SingleOrDefault(i => i.Id == id) is { } invoice
            ? InvoiceResponse.From(invoice, CompanyName)
            : null);

    public const string CompanyName = "Acme S.L.";

    // Paging is a SQL concern too

    public Task<PagedResult<InvoiceResponse>> ListAsync(
        Guid? clientCompanyId, InvoiceReviewStatus? reviewStatus, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(Guid clientCompanyId, string sha256, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved.Any(i => i.ClientCompanyId == clientCompanyId && i.Sha256 == sha256));

    /// <summary>Every company here has this NIF: the join with companies is a SQL concern.</summary>
    public static readonly TaxId CompanyTaxId = TaxId.Create("B12345674");

    /// <summary>Companies GetClientCompanyTaxIdAsync doesn't find, as if they were another firm's.</summary>
    public HashSet<Guid> UnknownCompanies { get; } = [];

    public Task<TaxId?> GetClientCompanyTaxIdAsync(
        Guid clientCompanyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UnknownCompanies.Contains(clientCompanyId) ? null : CompanyTaxId);

    // Filtering and ordering are SQL concerns (covered against SQL Server): here, approved invoices in upload order
    public Task<IReadOnlyList<InvoiceExportSource>> ListForExportAsync(
        Guid? clientCompanyId, DateOnly? from, DateOnly? to, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceExportSource>>(Saved
            .Where(i => i.ReviewStatus == InvoiceReviewStatus.Approved
                && (clientCompanyId is null || i.ClientCompanyId == clientCompanyId))
            .Take(max)
            .Select(i => new InvoiceExportSource(i, CompanyName, CompanyTaxId.Value))
            .ToList());

    public Task<IReadOnlyList<AiAccuracySample>> ListAiAccuracySamplesAsync(
        Guid? clientCompanyId, DateOnly? from, DateOnly? to, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiAccuracySample>>(Saved
            .Where(i => i.ReviewStatus == InvoiceReviewStatus.Approved
                && (clientCompanyId is null || i.ClientCompanyId == clientCompanyId))
            .Take(max)
            .Select(AiAccuracySample.From)
            .ToList());

    public Task<Guid?> FindDuplicateAsync(
        Guid invoiceId, Guid clientCompanyId, string supplierTaxId, string invoiceNumber,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Saved
            .Where(i => i.ClientCompanyId == clientCompanyId && i.Id != invoiceId
                && i.ExtractionStatus == InvoiceExtractionStatus.Extracted
                && i.ExtractedData?.SupplierTaxId == supplierTaxId
                && i.ExtractedData?.InvoiceNumber == invoiceNumber)
            .OrderBy(i => i.UploadedAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefault());

    public Task<string?> FindConfirmedLedgerAccountAsync(
        Guid clientCompanyId, string supplierTaxId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Confirmed(clientCompanyId)
            .FirstOrDefault(i => i.ExtractedData?.SupplierTaxId == supplierTaxId)?.LedgerAccountCode);

    public Task<IReadOnlyList<ConfirmedLedgerAccountExample>> ListConfirmedLedgerAccountsAsync(
        Guid clientCompanyId, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConfirmedLedgerAccountExample>>(Confirmed(clientCompanyId)
            .Take(max)
            .Select(i => new ConfirmedLedgerAccountExample(
                i.ExtractedData?.SupplierName, i.ExtractedData?.ItemsDescription, i.LedgerAccountCode!))
            .ToList());

    private IEnumerable<Invoice> Confirmed(Guid clientCompanyId) =>
        Saved.Where(i => i.ClientCompanyId == clientCompanyId && i.LedgerAccountCode is not null
                && i.LedgerAccountConfirmedBy is not null)
            .OrderByDescending(i => i.LedgerAccountConfirmedAt);

    public void Add(Invoice invoice) => _pending.Add(invoice);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailNextSave is { } error)
        {
            FailNextSave = null;
            _pending.Clear();
            throw error;
        }

        SaveCount++;
        var count = _pending.Count;
        Saved.AddRange(_pending);
        _pending.Clear();
        return Task.FromResult(count);
    }
}

internal sealed class InMemoryFileStorage : IFileStorage
{
    public Dictionary<string, (byte[] Content, string ContentType)> Files { get; } = [];

    public async Task UploadAsync(
        string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (Files.ContainsKey(path))
            throw new InvalidOperationException($"{path} already exists.");

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[path] = (copy.ToArray(), contentType);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(path, out var file) ? new MemoryStream(file.Content) : null);

    public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public static readonly Guid ObjectId = Guid.NewGuid();

    Guid ICurrentUser.ObjectId => ObjectId;
    public string? Name => "Ana García";
    public string? Email => "ana@gestoria.es";
}

internal static class TestFiles
{
    public static byte[] Pdf(string text = "factura") => [.. "%PDF-1.7\n"u8, .. System.Text.Encoding.UTF8.GetBytes(text)];

    public static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
}

internal sealed class RecordingExtractionQueue : IInvoiceExtractionQueue
{
    public List<InvoiceExtractionRequest> Requests { get; } = [];

    public Exception? FailWith { get; set; }

    public Task EnqueueAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } error)
            throw error;

        Requests.Add(request);
        return Task.CompletedTask;
    }
}

/// <summary>Returns <see cref="Result"/>, or throws <see cref="FailWith"/>. Records what it was given.</summary>
internal sealed class FakeExtractor : IInvoiceExtractor
{
    public ExtractedInvoiceData Result { get; set; } =
        ExtractedInvoiceData.Create(supplierName: "Papelería Sol", totalAmount: 121m);

    public Dictionary<InvoiceField, double> Confidence { get; set; } = new() { [InvoiceField.TotalAmount] = 0.9 };

    public Exception? FailWith { get; set; }

    public List<(byte[] Content, InvoiceFileFormat Format)> Calls { get; } = [];

    /// <summary>Runs inside the call, e.g. to see what was saved before the read started.</summary>
    public Action? OnExtract { get; set; }

    public async Task<InvoiceExtractionResult> ExtractAsync(
        Stream content, InvoiceFileFormat format, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Calls.Add((copy.ToArray(), format));
        OnExtract?.Invoke();

        if (FailWith is { } error)
            throw error;

        return new InvoiceExtractionResult(Result, Confidence);
    }
}

/// <summary>Returns <see cref="Result"/>, or throws <see cref="FailWith"/>. Records what it was asked.</summary>
internal sealed class FakeClassifier : ILedgerAccountClassifier
{
    public LedgerAccountClassification? Result { get; set; } = new("628", "Es una factura de electricidad.");

    public Exception? FailWith { get; set; }

    public List<LedgerAccountClassificationRequest> Requests { get; } = [];

    public Task<LedgerAccountClassification?> ClassifyAsync(
        LedgerAccountClassificationRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (FailWith is { } error)
            throw error;

        return Task.FromResult(Result);
    }
}
