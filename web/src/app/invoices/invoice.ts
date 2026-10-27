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
  /** When it was last queued to be read (the upload, or the latest retry). ISO 8601 with offset. */
  extractionQueuedAt: string;
  /** Why the last read failed, written for the user. Only when Failed. */
  extractionError: string | null;
  extractedAt: string | null;
  /** Only once Extracted. Every field can be missing: the reader may not find it. */
  extractedData: ExtractedInvoiceData | null;
  /** What the checks found when it was read. Empty: nothing to look at. */
  issues: InvoiceIssue[];
  /** The earlier invoice it looks like a copy of. */
  duplicateOfInvoiceId: string | null;
  /** What Plumy proposes, once Extracted. Kept after a person confirms an account. */
  ledgerAccountSuggestion: LedgerAccountSuggestion | null;
  /** Where the suggestion stands. Null until the invoice is read. */
  suggestionStatus: SuggestionStatus | null;
  /** Why nothing was suggested (in Spanish, often the AI's own words). Only when NoSuggestion, if known. */
  noSuggestionReason: string | null;
  /** The account a person confirmed. */
  ledgerAccount: LedgerAccount | null;
  /** How sure the reader was of each field it read, 0 to 1. A field without a value has none. */
  fieldConfidence: Partial<Record<InvoiceField, number>>;
  /** The fields a person changed: their value is the person's, not the reader's. */
  correctedFields: InvoiceField[];
  reviewStatus: ReviewStatus;
  /** When it was approved or rejected. */
  reviewedAt: string | null;
  /** Only while Rejected. */
  rejectionReason: string | null;
}

/** A field of the read data, by its JSON name: what confidence and corrections refer to. */
export type InvoiceField = keyof ExtractedInvoiceData;

/** Mirrors Plumy.Domain.Invoices.InvoiceReviewStatus. */
export type ReviewStatus = 'Unreviewed' | 'Approved' | 'Rejected';

export const reviewStatusLabels: Record<ReviewStatus, string> = {
  Unreviewed: 'Por revisar',
  Approved: 'Aprobada',
  Rejected: 'Rechazada',
};

/**
 * Below this, the review screen points the field out. Document Intelligence is usually above 0.9 on a clean
 * invoice; a value under 0.8 is worth a second look. A display choice: the API stores the raw confidence.
 */
export const lowConfidenceThreshold = 0.8;

/** The reader's confidence for a field a person hasn't touched, when it's low enough to say so. */
export function lowConfidence(invoice: Invoice, field: InvoiceField): number | null {
  if (invoice.correctedFields.includes(field)) {
    return null;
  }
  const confidence = invoice.fieldConfidence[field];
  return confidence !== undefined && confidence < lowConfidenceThreshold ? confidence : null;
}

/** Whether a person can still change it: read, and nobody approved or rejected it yet. */
export function isEditable(invoice: Invoice): boolean {
  return invoice.extractionStatus === 'Extracted' && invoice.reviewStatus === 'Unreviewed';
}

/** Same as the domain: read and with a confirmed account (the issues don't block it). */
export function canApprove(invoice: Invoice): boolean {
  return isEditable(invoice) && invoice.ledgerAccount !== null;
}

/** Same as the domain: read, or unreadable, and not decided yet. */
export function canReject(invoice: Invoice): boolean {
  return (
    invoice.reviewStatus === 'Unreviewed' &&
    (invoice.extractionStatus === 'Extracted' || invoice.extractionStatus === 'Failed')
  );
}

/** Same limit as Invoice.MaxRejectionReasonLength in the domain. */
export const maxRejectionReasonLength = 300;

/** Mirrors Plumy.Application.Invoices.LedgerAccountResponse: an account of the PGC. */
export interface LedgerAccount {
  code: string;
  name: string;
}

/** Mirrors Plumy.Domain.Invoices.LedgerAccountSuggestionStatus. */
export type SuggestionStatus = 'Pending' | 'Suggested' | 'NoSuggestion' | 'Failed';

/** Read, nobody picked an account yet, and the AI is still on it (a second or two after the read). */
export function isSuggestingAccount(invoice: Invoice): boolean {
  return (
    invoice.extractionStatus === 'Extracted' &&
    invoice.suggestionStatus === 'Pending' &&
    invoice.ledgerAccount === null
  );
}

/** Mirrors Plumy.Domain.Invoices.LedgerAccountSuggestionSource. */
export type LedgerAccountSuggestionSource = 'SupplierHistory' | 'Ai';

/** Mirrors Plumy.Application.Invoices.LedgerAccountSuggestionResponse. */
export interface LedgerAccountSuggestion extends LedgerAccount {
  source: LedgerAccountSuggestionSource;
  /** Only the AI explains itself. */
  reason: string | null;
  suggestedAt: string;
}

/** Mirrors Plumy.Domain.Invoices.InvoiceExtractionStatus. */
export type ExtractionStatus = 'Pending' | 'Processing' | 'Extracted' | 'Failed';

/** Mirrors Plumy.Application.Invoices.ExtractedInvoiceDataResponse. */
export interface ExtractedInvoiceData {
  supplierName: string | null;
  supplierTaxId: string | null;
  /** Who it is addressed to: should be the company's own NIF. */
  customerTaxId: string | null;
  invoiceNumber: string | null;
  /** yyyy-MM-dd. */
  issueDate: string | null;
  netAmount: number | null;
  taxAmount: number | null;
  totalAmount: number | null;
  /** ISO 4217, e.g. EUR. */
  currency: string | null;
  /** What was bought: the lines' descriptions, joined. */
  itemsDescription: string | null;
}

/** Mirrors Plumy.Domain.Invoices.InvoiceIssue. */
export type InvoiceIssue =
  | 'MissingSupplierTaxId'
  | 'InvalidSupplierTaxId'
  | 'CustomerTaxIdMismatch'
  | 'MissingInvoiceNumber'
  | 'MissingIssueDate'
  | 'FutureIssueDate'
  | 'MissingTotal'
  | 'AmountsMismatch'
  | 'UnusualTaxAmount'
  | 'PossibleDuplicate';

export const invoiceIssueLabels: Record<InvoiceIssue, string> = {
  MissingSupplierTaxId: 'Falta el NIF del proveedor',
  InvalidSupplierTaxId: 'El NIF del proveedor no es válido',
  CustomerTaxIdMismatch: 'Va dirigida a otro NIF, no a esta empresa',
  MissingInvoiceNumber: 'Falta el número de factura',
  MissingIssueDate: 'Falta la fecha',
  FutureIssueDate: 'La fecha es posterior a hoy',
  MissingTotal: 'Falta el total',
  AmountsMismatch: 'Base + IVA no da el total',
  UnusualTaxAmount: 'El IVA no corresponde a ningún tipo español',
  PossibleDuplicate: 'Posible duplicada: ya hay otra con el mismo proveedor y número',
};

/** Each issue on a line, for a tooltip. An issue the app doesn't know yet still shows, by its name. */
export function describeIssues(issues: readonly InvoiceIssue[]): string {
  return issues.map((issue) => invoiceIssueLabels[issue] ?? issue).join('\n');
}

export const extractionStatusLabels: Record<ExtractionStatus, string> = {
  Pending: 'Pendiente',
  Processing: 'Leyendo…',
  Extracted: 'Leída',
  Failed: 'Error',
};

/**
 * A Pending invoice normally leaves that state within seconds. Waiting longer than this since it was last queued,
 * queueing it probably failed, and the user can queue it again. Same value as Invoice.StuckPendingAfter in the
 * domain, which refuses an earlier retry.
 */
export const stuckPendingAfterMs = 5 * 60 * 1000;

/** Whether the list should offer to read it again (the API accepts it from Failed and Pending). */
export function canRetryExtraction(invoice: Invoice, now: number): boolean {
  if (invoice.extractionStatus === 'Failed') {
    return true;
  }
  return (
    invoice.extractionStatus === 'Pending' &&
    now - Date.parse(invoice.extractionQueuedAt) >= stuckPendingAfterMs
  );
}

/**
 * Whether the worker is (or should soon be) on it, reading it or suggesting its account: the list keeps
 * refreshing while any invoice is.
 */
export function isExtractionInProgress(invoice: Invoice, now: number): boolean {
  return (
    invoice.extractionStatus === 'Processing' ||
    (invoice.extractionStatus === 'Pending' && !canRetryExtraction(invoice, now)) ||
    isSuggestingAccount(invoice)
  );
}

export const invoicesUrl = '/api/invoices';

/** Query of GET /api/invoices. Without a status, every invoice. */
export interface InvoiceListParams {
  page: number;
  pageSize: number;
  reviewStatus?: ReviewStatus;
}

export const retryExtractionUrl = (id: string) => `${invoicesUrl}/${id}/extraction`;

export const invoiceUrl = (id: string) => `${invoicesUrl}/${id}`;

export const confirmLedgerAccountUrl = (id: string) => `${invoicesUrl}/${id}/ledger-account`;

export const invoiceDataUrl = (id: string) => `${invoicesUrl}/${id}/data`;

export const reviewInvoiceUrl = (id: string) => `${invoicesUrl}/${id}/review`;

export const ledgerAccountsUrl = '/api/ledger-accounts';

export const exportInvoicesUrl = `${invoicesUrl}/export`;

export type InvoiceExportFormat = 'xlsx' | 'csv';

/** Query of GET /api/invoices/export. Without a company, every company; dates are yyyy-MM-dd, inclusive. */
export interface InvoiceExportParams {
  clientCompanyId?: string;
  from?: string;
  to?: string;
  format: InvoiceExportFormat;
}

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
