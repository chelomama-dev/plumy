import {
  canApprove,
  canReject,
  canRetryExtraction,
  checkInvoiceFile,
  describeIssues,
  ExtractionStatus,
  InvoiceIssue,
  Invoice,
  isEditable,
  isExtractionInProgress,
  lowConfidence,
  maxInvoiceFileSizeBytes,
  stuckPendingAfterMs,
} from './invoice';

describe('checkInvoiceFile', () => {
  const file = (name: string, size = 1000) => {
    const f = new File(['x'], name);
    Object.defineProperty(f, 'size', { value: size });
    return f;
  };

  it('accepts PDF, JPG and PNG whatever the case of the extension', () => {
    for (const name of ['factura.pdf', 'FOTO.JPG', 'ticket.jpeg', 'escaneo.Png']) {
      expect(checkInvoiceFile(file(name))).toBeNull();
    }
  });

  it('rejects other extensions', () => {
    expect(checkInvoiceFile(file('factura.docx'))).toBe('Solo se aceptan PDF, JPG y PNG.');
    expect(checkInvoiceFile(file('pdf'))).toBe('Solo se aceptan PDF, JPG y PNG.');
  });

  it('rejects empty files and files over 10 MB', () => {
    expect(checkInvoiceFile(file('factura.pdf', 0))).toBe('El archivo está vacío.');
    expect(checkInvoiceFile(file('factura.pdf', maxInvoiceFileSizeBytes))).toBeNull();
    expect(checkInvoiceFile(file('factura.pdf', maxInvoiceFileSizeBytes + 1))).toBe(
      'Supera el máximo de 10 MB.',
    );
  });
});

describe('extraction status', () => {
  const extractionQueuedAt = '2026-10-24T09:00:00Z';
  const uploaded = Date.parse(extractionQueuedAt);
  const withStatus = (extractionStatus: ExtractionStatus) =>
    ({ extractionStatus, uploadedAt: '2026-10-20T09:00:00Z', extractionQueuedAt }) as Invoice;

  it('can retry a failed invoice, never one being read or already read', () => {
    expect(canRetryExtraction(withStatus('Failed'), uploaded)).toBe(true);
    expect(canRetryExtraction(withStatus('Processing'), uploaded + stuckPendingAfterMs)).toBe(
      false,
    );
    expect(canRetryExtraction(withStatus('Extracted'), uploaded + stuckPendingAfterMs)).toBe(false);
  });

  it('counts the wait from the latest queueing, not from the upload', () => {
    // Uploaded days ago and retried just now: retrying again would read it twice
    expect(canRetryExtraction(withStatus('Pending'), uploaded + 1000)).toBe(false);
  });

  it('can retry a Pending invoice only once it has waited too long', () => {
    expect(canRetryExtraction(withStatus('Pending'), uploaded + stuckPendingAfterMs - 1)).toBe(
      false,
    );
    expect(canRetryExtraction(withStatus('Pending'), uploaded + stuckPendingAfterMs)).toBe(true);
  });

  it('is in progress while the AI is still choosing the account of a read invoice', () => {
    const read = (overrides: Partial<Invoice>) =>
      ({ ...withStatus('Extracted'), ledgerAccount: null, ...overrides }) as Invoice;

    expect(isExtractionInProgress(read({ suggestionStatus: 'Pending' }), uploaded)).toBe(true);
    expect(isExtractionInProgress(read({ suggestionStatus: 'NoSuggestion' }), uploaded)).toBe(
      false,
    );
    // A person picked it first: nothing left to wait for
    expect(
      isExtractionInProgress(
        read({ suggestionStatus: 'Pending', ledgerAccount: { code: '629', name: 'Otros' } }),
        uploaded,
      ),
    ).toBe(false);
  });

  it('is in progress while Processing or recently Pending', () => {
    expect(isExtractionInProgress(withStatus('Processing'), uploaded)).toBe(true);
    expect(isExtractionInProgress(withStatus('Pending'), uploaded)).toBe(true);
    expect(isExtractionInProgress(withStatus('Pending'), uploaded + stuckPendingAfterMs)).toBe(
      false,
    );
    expect(isExtractionInProgress(withStatus('Extracted'), uploaded)).toBe(false);
    expect(isExtractionInProgress(withStatus('Failed'), uploaded)).toBe(false);
  });
});

describe('describeIssues', () => {
  it('puts each issue on its own line, in Spanish', () => {
    expect(describeIssues(['MissingTotal', 'PossibleDuplicate'])).toBe(
      'Falta el total\nPosible duplicada: ya hay otra con el mismo proveedor y número',
    );
  });

  it('shows an issue it does not know yet by its name', () => {
    expect(describeIssues(['SomethingNew' as InvoiceIssue])).toBe('SomethingNew');
  });

  it('is empty without issues', () => {
    expect(describeIssues([])).toBe('');
  });
});

describe('review', () => {
  const read = (overrides: Partial<Invoice> = {}) =>
    ({
      extractionStatus: 'Extracted',
      reviewStatus: 'Unreviewed',
      approvedAutomatically: false,
      ledgerAccount: null,
      fieldConfidence: { totalAmount: 0.42, supplierName: 0.98, invoiceNumber: 0.5 },
      correctedFields: ['invoiceNumber'],
      ...overrides,
    }) as Invoice;

  it('points out a field the reader was unsure of, unless a person already changed it', () => {
    expect(lowConfidence(read(), 'totalAmount')).toBe(0.42);
    expect(lowConfidence(read(), 'supplierName')).toBeNull();
    expect(lowConfidence(read(), 'invoiceNumber')).toBeNull();
    expect(lowConfidence(read(), 'issueDate')).toBeNull();
  });

  it('can be edited only while read and undecided', () => {
    expect(isEditable(read())).toBe(true);
    expect(isEditable(read({ reviewStatus: 'Approved' }))).toBe(false);
    expect(isEditable(read({ extractionStatus: 'Failed' }))).toBe(false);
  });

  it('can be approved only with a confirmed account', () => {
    expect(canApprove(read())).toBe(false);
    expect(canApprove(read({ ledgerAccount: { code: '628', name: 'Suministros' } }))).toBe(true);
  });

  it('can be rejected when read or unreadable, and undecided', () => {
    expect(canReject(read())).toBe(true);
    expect(canReject(read({ extractionStatus: 'Failed' }))).toBe(true);
    expect(canReject(read({ extractionStatus: 'Processing' }))).toBe(false);
    expect(canReject(read({ reviewStatus: 'Rejected' }))).toBe(false);
  });
});
