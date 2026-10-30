using System.Text.RegularExpressions;
using Plumy.Domain.Accounting;
using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>
/// A supplier invoice received by a client company, uploaded as a file. Its data is read from the file in the
/// background: <see cref="ExtractionStatus"/> says how far that has gone.
/// </summary>
public sealed partial class Invoice
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 255;
    public const int Sha256Length = 64;
    public const int MaxStoragePathLength = 120;
    public const int MaxExtractionErrorLength = 500;
    public const int MaxRejectionReasonLength = 300;
    public const int MaxNoSuggestionReasonLength = 300;

    /// <summary>
    /// How long a Pending invoice waits for the worker before a person may queue it again. Longer than the queue
    /// spends on one message (5 deliveries, one a minute): sooner, the retry would race the first message.
    /// </summary>
    public static readonly TimeSpan StuckPendingAfter = TimeSpan.FromMinutes(5);

    public Guid Id { get; private set; }

    /// <summary>The accounting firm (the tenant). Redundant with the company's, but every tenant filter needs it.</summary>
    public Guid FirmId { get; private set; }

    public Guid ClientCompanyId { get; private set; }

    /// <summary>The name the file had on the uploader's machine. Only for display: never used as a path.</summary>
    public string FileName { get; private set; }

    public InvoiceFileFormat Format { get; private set; }
    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the content (lowercase hex): the same file uploaded twice has the same hash.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Where the file lives in storage. Built from ids only, so user input never reaches a path.</summary>
    public string StoragePath { get; private set; }

    /// <summary>Entra object id (<c>oid</c>) of who uploaded it.</summary>
    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public InvoiceExtractionStatus ExtractionStatus { get; private set; } = InvoiceExtractionStatus.Pending;

    /// <summary>What was read from the file. Only set once <see cref="ExtractionStatus"/> is Extracted.</summary>
    public ExtractedInvoiceData? ExtractedData { get; private set; }

    /// <summary>
    /// Identifies the latest time the invoice was queued for reading (the upload, then each retry). Every message
    /// carries it, so the worker can tell a message from an earlier queueing and drop it instead of reading the
    /// invoice twice in parallel (a fencing token).
    /// </summary>
    public Guid ExtractionAttemptId { get; private set; }

    /// <summary>When the invoice was last queued for reading: what "it has been waiting too long" counts from.</summary>
    public DateTimeOffset ExtractionQueuedAt { get; private set; }

    /// <summary>Why the last extraction failed, written for the person reviewing it. Only set when Failed.</summary>
    public string? ExtractionError { get; private set; }

    public DateTimeOffset? ExtractedAt { get; private set; }

    private readonly Dictionary<InvoiceField, double> _fieldConfidence = [];

    /// <summary>
    /// How sure the reader was of each field it read, from 0 to 1. A field it didn't read has none. Kept as read even
    /// after a person corrects the field: <see cref="CorrectedFields"/> says which ones that happened to.
    /// </summary>
    public IReadOnlyDictionary<InvoiceField, double> FieldConfidence => _fieldConfidence;

    private readonly List<InvoiceField> _correctedFields = [];

    /// <summary>The fields a person changed: their value is the person's, not the reader's.</summary>
    public IReadOnlyList<InvoiceField> CorrectedFields => _correctedFields;

    private readonly List<InvoiceIssue> _issues = [];

    /// <summary>What the checks found when it was read (<see cref="InvoiceValidator"/>). Empty: nothing to look at.</summary>
    public IReadOnlyList<InvoiceIssue> Issues => _issues;

    /// <summary>The earlier invoice of the same company with the same supplier and number, if any.</summary>
    public Guid? DuplicateOfInvoiceId { get; private set; }

    /// <summary>The account Plumy proposes. Only once Extracted, and only until a person confirms one.</summary>
    public LedgerAccountSuggestion? LedgerAccountSuggestion { get; private set; }

    /// <summary>Where the suggestion stands. Null until the invoice is read.</summary>
    public LedgerAccountSuggestionStatus? SuggestionStatus { get; private set; }

    /// <summary>
    /// Why nothing was suggested, for the reviewer (in Spanish: usually the AI's own words). Only while
    /// <see cref="SuggestionStatus"/> is NoSuggestion, and not always known.
    /// </summary>
    public string? NoSuggestionReason { get; private set; }

    /// <summary>
    /// The account a person chose (the suggestion or another). Only these teach the suggestions: an unconfirmed
    /// suggestion is never used as history.
    /// </summary>
    public string? LedgerAccountCode { get; private set; }

    /// <summary>
    /// Entra object id (<c>oid</c>) of who confirmed the account. Null when Plumy set it by approving the invoice
    /// automatically: then it isn't a person's choice, and it teaches the suggestions nothing.
    /// </summary>
    public Guid? LedgerAccountConfirmedBy { get; private set; }

    public DateTimeOffset? LedgerAccountConfirmedAt { get; private set; }

    public InvoiceReviewStatus ReviewStatus { get; private set; } = InvoiceReviewStatus.Unreviewed;

    /// <summary>
    /// Entra object id (<c>oid</c>) of who approved or rejected it. Only while Approved or Rejected, and not when
    /// <see cref="ApprovedAutomatically"/>: nobody did.
    /// </summary>
    public Guid? ReviewedBy { get; private set; }

    /// <summary>
    /// Plumy approved it with nobody looking, under the firm's <see cref="AutoApprovalPolicy"/>. Only while Approved:
    /// once a person reopens it, it is theirs.
    /// </summary>
    public bool ApprovedAutomatically { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Why it won't be booked, written by the person who rejected it. Only while Rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// Changes on every save (a SQL Server rowversion, set by the database). Whoever changes the invoice from a screen
    /// sends back the version it read, so a change made on an out-of-date copy is refused instead of silently
    /// overwriting someone else's.
    /// </summary>
    public byte[] Version { get; private set; } = [];

    /// <summary>Read, with no account yet: the worker still has to suggest one.</summary>
    public bool NeedsLedgerAccountSuggestion =>
        ExtractionStatus == InvoiceExtractionStatus.Extracted
        && SuggestionStatus == LedgerAccountSuggestionStatus.Pending
        && LedgerAccountCode is null;

    private Invoice(
        Guid id, Guid firmId, Guid clientCompanyId, string fileName, InvoiceFileFormat format, long sizeBytes,
        string sha256, Guid uploadedBy, DateTimeOffset uploadedAt)
    {
        Id = id;
        FirmId = firmId;
        ClientCompanyId = clientCompanyId;
        FileName = fileName;
        Format = format;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StoragePath = $"{firmId}/{clientCompanyId}/{id}";
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
        ExtractionAttemptId = Guid.CreateVersion7();
        ExtractionQueuedAt = uploadedAt;
    }

    public static Invoice Create(
        Guid firmId, Guid clientCompanyId, string? fileName, InvoiceFileFormat format, long sizeBytes,
        string? sha256, Guid uploadedBy, DateTimeOffset uploadedAt)
    {
        if (firmId == Guid.Empty)
            throw new DomainException("The invoice must belong to a firm.");
        if (clientCompanyId == Guid.Empty)
            throw new DomainException("The invoice must belong to a client company.");
        if (uploadedBy == Guid.Empty)
            throw new DomainException("The invoice must have an uploader.");
        if (!Enum.IsDefined(format))
            throw new DomainException("Unknown file format.");
        if (sha256 is null || !Sha256Pattern().IsMatch(sha256))
            throw new DomainException("The file hash must be a lowercase hex SHA-256.");

        EnsureValidSize(sizeBytes);

        return new Invoice(
            Guid.CreateVersion7(), firmId, clientCompanyId, NormalizeFileName(fileName), format, sizeBytes, sha256,
            uploadedBy, uploadedAt);
    }

    /// <summary>
    /// The worker takes the invoice. Also valid when it is already Processing: messages are delivered at least once,
    /// and a worker that crashed mid-way leaves it there for the redelivery to pick up.
    /// </summary>
    public void StartExtraction()
    {
        if (ExtractionStatus is not (InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Processing))
            throw new DomainException($"An invoice in status {ExtractionStatus} can't start an extraction.");

        ExtractionStatus = InvoiceExtractionStatus.Processing;
    }

    /// <param name="validation">What <see cref="InvoiceValidator"/> found in <paramref name="data"/>.</param>
    /// <param name="confidence">The reader's confidence per field, from 0 to 1. None: it doesn't give any.</param>
    public void CompleteExtraction(
        ExtractedInvoiceData data, InvoiceValidation validation, DateTimeOffset extractedAt,
        IReadOnlyDictionary<InvoiceField, double>? confidence = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureProcessing();
        EnsureValidConfidence(confidence);
        ApplyValidation(validation);

        ExtractionStatus = InvoiceExtractionStatus.Extracted;
        SuggestionStatus = LedgerAccountSuggestionStatus.Pending;
        NoSuggestionReason = null;
        ExtractedData = data;
        ExtractionError = null;
        ExtractedAt = extractedAt;
        _fieldConfidence.Clear();
        foreach (var (field, value) in confidence ?? new Dictionary<InvoiceField, double>())
            _fieldConfidence[field] = value;
        _correctedFields.Clear();
    }

    /// <summary>
    /// A person fixes what was read. <paramref name="data"/> is the whole invoice as they left it, so the checks run
    /// again on it; the fields that differ from the current values are marked as corrected.
    /// </summary>
    /// <param name="validation">What <see cref="InvoiceValidator"/> found in <paramref name="data"/>.</param>
    public void CorrectData(ExtractedInvoiceData data, InvoiceValidation validation)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (ExtractionStatus != InvoiceExtractionStatus.Extracted || ExtractedData is null)
            throw new DomainException("Only an invoice that has been read can be corrected.");
        EnsureUnreviewed();
        ApplyValidation(validation);

        foreach (var field in ExtractedData.FieldsChangedIn(data).Where(field => !_correctedFields.Contains(field)))
            _correctedFields.Add(field);
        ExtractedData = data;
    }

    /// <summary>
    /// A person checked the invoice: it goes to the books. Needs a confirmed account (an invoice can't be booked
    /// without one); the issues don't block it, they are warnings, and the person has seen them.
    /// </summary>
    public void Approve(Guid approvedBy, DateTimeOffset approvedAt)
    {
        if (ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new DomainException("Only an invoice that has been read can be approved.");
        if (LedgerAccountCode is null)
            throw new DomainException("Confirm the invoice's ledger account before approving it.");
        EnsureUnreviewed();

        SetReview(InvoiceReviewStatus.Approved, approvedBy, approvedAt, rejectionReason: null);

        // An account Plumy set when it approved the invoice, reopened and approved again: the person kept it
        if (LedgerAccountConfirmedBy is null)
        {
            LedgerAccountConfirmedBy = approvedBy;
            LedgerAccountConfirmedAt = approvedAt;
        }
    }

    /// <summary>
    /// Whether Plumy may approve the invoice by itself: the firm allows it, and there is nothing a person would look
    /// at. That is: just read, untouched, no issue at all (no duplicate, amounts that add up, a valid NIF...), the
    /// account the company already uses for this supplier (never the language model's guess), and every booked field
    /// read with at least the policy's confidence. A reader that gives no confidence never qualifies.
    /// </summary>
    public bool CanBeApprovedAutomatically(AutoApprovalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return policy.Enabled
            && ExtractionStatus == InvoiceExtractionStatus.Extracted
            && ExtractedData is { } data
            && ReviewStatus == InvoiceReviewStatus.Unreviewed
            && LedgerAccountCode is null
            && _correctedFields.Count == 0
            && _issues.Count == 0
            && DuplicateOfInvoiceId is null
            && SuggestionStatus == LedgerAccountSuggestionStatus.Suggested
            && LedgerAccountSuggestion is { Source: LedgerAccountSuggestionSource.SupplierHistory }
            && AutoApprovalPolicy.BookedFields.All(field =>
                data.Has(field)
                && _fieldConfidence.TryGetValue(field, out var confidence)
                && confidence >= policy.MinConfidence);
    }

    /// <summary>
    /// Plumy approves the invoice by itself: it takes the suggested account and approves it, as a person would with
    /// nothing to correct. Only when <see cref="CanBeApprovedAutomatically"/>. A person can still reopen it.
    /// </summary>
    public void ApproveAutomatically(AutoApprovalPolicy policy, DateTimeOffset approvedAt)
    {
        if (!CanBeApprovedAutomatically(policy))
            throw new DomainException("The invoice doesn't meet the firm's rules to be approved automatically.");

        LedgerAccountCode = LedgerAccountSuggestion!.Code;
        LedgerAccountConfirmedBy = null;
        LedgerAccountConfirmedAt = approvedAt;

        ReviewStatus = InvoiceReviewStatus.Approved;
        ReviewedBy = null;
        ReviewedAt = approvedAt;
        RejectionReason = null;
        ApprovedAutomatically = true;
    }

    /// <summary>
    /// A person decides the invoice won't be booked. Also when it couldn't be read: an unreadable file is often not an
    /// invoice at all.
    /// </summary>
    public void Reject(string? reason, Guid rejectedBy, DateTimeOffset rejectedAt)
    {
        if (ExtractionStatus is not (InvoiceExtractionStatus.Extracted or InvoiceExtractionStatus.Failed))
            throw new DomainException("Only an invoice that has been read, or couldn't be, can be rejected.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection needs a reason.");
        EnsureUnreviewed();

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxRejectionReasonLength)
            throw new DomainException($"The reason cannot exceed {MaxRejectionReasonLength} characters.");

        SetReview(InvoiceReviewStatus.Rejected, rejectedBy, rejectedAt, trimmed);
    }

    /// <summary>A person takes back their approval or rejection: the invoice can be changed again.</summary>
    public void Reopen()
    {
        if (ReviewStatus == InvoiceReviewStatus.Unreviewed)
            throw new DomainException("The invoice hasn't been approved or rejected.");

        ReviewStatus = InvoiceReviewStatus.Unreviewed;
        ReviewedBy = null;
        ReviewedAt = null;
        RejectionReason = null;
        ApprovedAutomatically = false;
    }

    public void FailExtraction(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failed extraction needs a reason.");
        EnsureProcessing();

        var trimmed = reason.Trim();
        ExtractionStatus = InvoiceExtractionStatus.Failed;
        ExtractionError = trimmed.Length > MaxExtractionErrorLength ? trimmed[..MaxExtractionErrorLength] : trimmed;
    }

    /// <summary>Pending for longer than <see cref="StuckPendingAfter"/>: queueing it probably failed.</summary>
    public bool IsStuckInQueue(DateTimeOffset now) =>
        ExtractionStatus == InvoiceExtractionStatus.Pending && now - ExtractionQueuedAt >= StuckPendingAfter;

    /// <summary>
    /// Puts the invoice back in the queue's hands, as a new attempt. From Failed, to try again; from Pending only
    /// once it is stuck there, because queueing it may have failed after the upload was saved. Never from
    /// Processing (a worker has it) or Extracted (it's done).
    /// </summary>
    public void RetryExtraction(DateTimeOffset now)
    {
        if (ExtractionStatus is not (InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Failed))
            throw new DomainException($"An invoice in status {ExtractionStatus} can't be retried.");

        // A message for it is probably still on its way: a second one would read it twice
        if (ExtractionStatus == InvoiceExtractionStatus.Pending && !IsStuckInQueue(now))
            throw new DomainException("The invoice was queued moments ago and is about to be read.");

        // A rejected one stays as the person left it; reopen it first
        EnsureUnreviewed();

        ExtractionStatus = InvoiceExtractionStatus.Pending;
        ExtractionError = null;
        ExtractionAttemptId = Guid.CreateVersion7();
        ExtractionQueuedAt = now;
    }

    public void SuggestLedgerAccount(LedgerAccountSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new DomainException("Only an invoice that has been read can get a suggested account.");
        // A person's choice always wins over a machine's
        if (LedgerAccountCode is not null)
            throw new DomainException("The invoice's account has already been confirmed.");

        LedgerAccountSuggestion = suggestion;
        SuggestionStatus = LedgerAccountSuggestionStatus.Suggested;
        NoSuggestionReason = null;
    }

    /// <summary>Nothing to suggest, on purpose: too little was read, or the AI couldn't tell.</summary>
    /// <param name="reason">Why, for the reviewer, if known. Trimmed, and cut to <see cref="MaxNoSuggestionReasonLength"/>.</param>
    public void DeclineLedgerAccountSuggestion(string? reason)
    {
        EnsurePendingSuggestion();

        var trimmed = reason?.Trim();
        SuggestionStatus = LedgerAccountSuggestionStatus.NoSuggestion;
        NoSuggestionReason = string.IsNullOrEmpty(trimmed) ? null
            : trimmed.Length > MaxNoSuggestionReasonLength ? trimmed[..MaxNoSuggestionReasonLength].TrimEnd()
            : trimmed;
    }

    /// <summary>The suggestion kept failing: the invoice is left for a person to pick the account.</summary>
    public void FailLedgerAccountSuggestion()
    {
        EnsurePendingSuggestion();
        SuggestionStatus = LedgerAccountSuggestionStatus.Failed;
    }

    private void EnsurePendingSuggestion()
    {
        if (ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new DomainException("Only an invoice that has been read has a suggestion to settle.");
        if (SuggestionStatus != LedgerAccountSuggestionStatus.Pending)
            throw new DomainException($"The suggestion is already settled ({SuggestionStatus}).");
    }

    /// <summary>
    /// A person sets the account: accepting the suggestion, picking another, or changing their mind later.
    /// </summary>
    public void ConfirmLedgerAccount(string? code, Guid confirmedBy, DateTimeOffset confirmedAt)
    {
        if (ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new DomainException("Only an invoice that has been read can have its account confirmed.");
        if (ChartOfAccounts.Find(code) is null)
            throw new DomainException($"Unknown ledger account '{code}'.");
        if (confirmedBy == Guid.Empty)
            throw new DomainException("The account must be confirmed by someone.");
        EnsureUnreviewed();

        LedgerAccountCode = code;
        LedgerAccountConfirmedBy = confirmedBy;
        LedgerAccountConfirmedAt = confirmedAt;
    }

    private void ApplyValidation(InvoiceValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.DuplicateOfInvoiceId == Id)
            throw new DomainException("An invoice can't be a duplicate of itself.");

        _issues.Clear();
        _issues.AddRange(validation.Issues.Distinct());
        DuplicateOfInvoiceId = validation.DuplicateOfInvoiceId;
    }

    private void SetReview(InvoiceReviewStatus status, Guid reviewedBy, DateTimeOffset reviewedAt, string? rejectionReason)
    {
        if (reviewedBy == Guid.Empty)
            throw new DomainException("The invoice must be reviewed by someone.");

        ReviewStatus = status;
        ApprovedAutomatically = false;
        ReviewedBy = reviewedBy;
        ReviewedAt = reviewedAt;
        RejectionReason = rejectionReason;
    }

    // Approved and rejected invoices are decided (by a person, or by Plumy under the firm's rules): nothing changes them
    // until a person takes it back
    private void EnsureUnreviewed()
    {
        if (ReviewStatus != InvoiceReviewStatus.Unreviewed)
            throw new DomainException($"The invoice is {ReviewStatus}: reopen it to change it.");
    }

    private static void EnsureValidConfidence(IReadOnlyDictionary<InvoiceField, double>? confidence)
    {
        if (confidence is null)
            return;
        if (confidence.Keys.Any(field => !Enum.IsDefined(field)))
            throw new DomainException("Unknown invoice field.");
        if (confidence.Values.Any(value => !(value is >= 0 and <= 1)))
            throw new DomainException("A field's confidence must be between 0 and 1.");
    }

    private void EnsureProcessing()
    {
        if (ExtractionStatus != InvoiceExtractionStatus.Processing)
            throw new DomainException($"The invoice is {ExtractionStatus}, not being processed.");
    }

    /// <summary>Public so the upload can reject a file before reading it.</summary>
    public static void EnsureValidSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
            throw new DomainException("The file is empty.");
        if (sizeBytes > MaxFileSizeBytes)
            throw new DomainException($"The file cannot exceed {MaxFileSizeBytes / (1024 * 1024)} MB.");
    }

    // Keeps what a person would recognise: no folders (some browsers send the full path), no control characters.
    // A name that is too long is shortened, keeping its extension, instead of rejecting the upload
    private static string NormalizeFileName(string? fileName)
    {
        var name = fileName ?? "";
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length == 0)
            throw new DomainException("The file name is required.");

        if (name.Length <= MaxFileNameLength)
            return name;

        var extension = Path.GetExtension(name);
        if (extension.Length >= MaxFileNameLength / 2)
            extension = "";

        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
