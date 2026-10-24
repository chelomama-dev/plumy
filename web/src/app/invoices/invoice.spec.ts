import {
  canRetryExtraction,
  checkInvoiceFile,
  describeIssues,
  ExtractionStatus,
  InvoiceIssue,
  Invoice,
  isExtractionInProgress,
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
  const uploadedAt = '2026-10-24T09:00:00Z';
  const uploaded = Date.parse(uploadedAt);
  const withStatus = (extractionStatus: ExtractionStatus) =>
    ({ extractionStatus, uploadedAt }) as Invoice;

  it('can retry a failed invoice, never one being read or already read', () => {
    expect(canRetryExtraction(withStatus('Failed'), uploaded)).toBe(true);
    expect(canRetryExtraction(withStatus('Processing'), uploaded + stuckPendingAfterMs)).toBe(
      false,
    );
    expect(canRetryExtraction(withStatus('Extracted'), uploaded + stuckPendingAfterMs)).toBe(false);
  });

  it('can retry a Pending invoice only once it has waited too long', () => {
    expect(canRetryExtraction(withStatus('Pending'), uploaded + stuckPendingAfterMs - 1)).toBe(
      false,
    );
    expect(canRetryExtraction(withStatus('Pending'), uploaded + stuckPendingAfterMs)).toBe(true);
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
