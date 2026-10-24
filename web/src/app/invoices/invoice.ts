/** Mirrors Plumy.Application.Invoices.InvoiceResponse. */
export interface Invoice {
  id: string;
  clientCompanyId: string;
  clientCompanyName: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** ISO 8601 with offset. */
  uploadedAt: string;
  extractionStatus: ExtractionStatus;
  /** Why the last read failed, written for the user. Only when Failed. */
  extractionError: string | null;
  extractedAt: string | null;
  /** Only once Extracted. Every field can be missing: the reader may not find it. */
  extractedData: ExtractedInvoiceData | null;
}

/** Mirrors Plumy.Domain.Invoices.InvoiceExtractionStatus. */
export type ExtractionStatus = 'Pending' | 'Processing' | 'Extracted' | 'Failed';

/** Mirrors Plumy.Application.Invoices.ExtractedInvoiceDataResponse. */
export interface ExtractedInvoiceData {
  supplierName: string | null;
  supplierTaxId: string | null;
  invoiceNumber: string | null;
  /** yyyy-MM-dd. */
  issueDate: string | null;
  netAmount: number | null;
  taxAmount: number | null;
  totalAmount: number | null;
  /** ISO 4217, e.g. EUR. */
  currency: string | null;
}

export const extractionStatusLabels: Record<ExtractionStatus, string> = {
  Pending: 'Pendiente',
  Processing: 'Leyendo…',
  Extracted: 'Leída',
  Failed: 'Error',
};

/**
 * A Pending invoice normally leaves that state within seconds. Older than this, queueing it probably failed
 * after the upload was saved, and the user can queue it again.
 */
export const stuckPendingAfterMs = 5 * 60 * 1000;

/** Whether the list should offer to read it again (the API accepts it from Failed and Pending). */
export function canRetryExtraction(invoice: Invoice, now: number): boolean {
  if (invoice.extractionStatus === 'Failed') {
    return true;
  }
  return (
    invoice.extractionStatus === 'Pending' &&
    now - Date.parse(invoice.uploadedAt) >= stuckPendingAfterMs
  );
}

/** Whether the worker is (or should soon be) on it: the list keeps refreshing while any invoice is. */
export function isExtractionInProgress(invoice: Invoice, now: number): boolean {
  return (
    invoice.extractionStatus === 'Processing' ||
    (invoice.extractionStatus === 'Pending' && !canRetryExtraction(invoice, now))
  );
}

export const invoicesUrl = '/api/invoices';

export const retryExtractionUrl = (id: string) => `${invoicesUrl}/${id}/extraction`;

export const uploadInvoiceUrl = (clientCompanyId: string) =>
  `/api/client-companies/${clientCompanyId}/invoices`;

/** Same limit as Invoice.MaxFileSizeBytes in the domain. */
export const maxInvoiceFileSizeBytes = 10 * 1024 * 1024;

/**
 * For the file picker and a first check before uploading. The API decides from the file's content, so a renamed
 * file is still rejected there.
 */
export const acceptedInvoiceFiles = '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png';

const acceptedExtensions = ['.pdf', '.jpg', '.jpeg', '.png'];

/** Why the browser can already tell this file won't be accepted, or null if it looks fine. */
export function checkInvoiceFile(file: File): string | null {
  const name = file.name.toLowerCase();
  if (!acceptedExtensions.some((extension) => name.endsWith(extension))) {
    return 'Solo se aceptan PDF, JPG y PNG.';
  }
  if (file.size === 0) {
    return 'El archivo está vacío.';
  }
  if (file.size > maxInvoiceFileSizeBytes) {
    return 'Supera el máximo de 10 MB.';
  }
  return null;
}
