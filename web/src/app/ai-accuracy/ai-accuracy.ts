import { InvoiceField, invoicesUrl, lowConfidenceThreshold } from '../invoices/invoice';

/** Mirrors Plumy.Application.Invoices.AiAccuracy.AiAccuracyReport. */
export interface AiAccuracyReport {
  /** How many approved invoices it is about: a person checked each one, so what they kept is the right answer. */
  approvedInvoices: number;
  /** Approved by Plumy itself in the period: left out of everything else, nobody checked them. */
  autoApprovedInvoices: number;
  ledgerAccounts: LedgerAccountAccuracy;
  /** Every field of the invoice, in the API's order. */
  fields: FieldAccuracy[];
  /** Fields by the reader's confidence, low to high. */
  confidenceBands: ConfidenceBandAccuracy[];
}

export interface LedgerAccountAccuracy {
  /** The language model's suggestions. */
  ai: SuggestionAccuracy;
  /** The account last confirmed for the same supplier, repeated. No AI involved. */
  supplierHistory: SuggestionAccuracy;
  /** Approved with nothing suggested: the AI declined, failed, or a person was faster. */
  notSuggested: number;
}

export interface SuggestionAccuracy {
  suggested: number;
  /** The confirmed account is the suggested one. */
  accepted: number;
}

export interface FieldAccuracy {
  field: InvoiceField;
  /** The reader returned a value. */
  read: number;
  /** Of those, how many a person changed. */
  corrected: number;
  /** The reader returned nothing and a person filled it in. */
  missed: number;
}

/** Fields read with a confidence in [from, to); the last band includes 1. */
export interface ConfidenceBandAccuracy {
  from: number;
  to: number;
  read: number;
  corrected: number;
}

export const aiAccuracyUrl = `${invoicesUrl}/ai-accuracy`;

/** How far back to look, in days. Null: every approved invoice. */
export type Period = 30 | 90 | 365 | null;

export const periodLabels: { value: Period; label: string }[] = [
  { value: 30, label: '30 días' },
  { value: 90, label: '90 días' },
  { value: 365, label: '1 año' },
  { value: null, label: 'Todo' },
];

/** The first day (yyyy-MM-dd, UTC, as the API counts) of a period ending today. */
export function periodStart(days: number, today: Date): string {
  const start = new Date(today);
  start.setUTCDate(start.getUTCDate() - (days - 1));
  return start.toISOString().slice(0, 10);
}

/** A share from 0 to 1, or null when there is nothing to divide (no "0 %" out of nothing). */
export function rate(part: number, whole: number): number | null {
  return whole > 0 ? part / whole : null;
}

/** Below the review screen's threshold: the fields in this band are pointed out as doubtful. */
export function isDoubtfulBand(band: ConfidenceBandAccuracy): boolean {
  return band.to <= lowConfidenceThreshold;
}

export const fieldLabels: Record<InvoiceField, string> = {
  supplierName: 'Nombre del proveedor',
  supplierTaxId: 'NIF del proveedor',
  customerTaxId: 'NIF del destinatario',
  invoiceNumber: 'Número',
  issueDate: 'Fecha',
  netAmount: 'Base imponible',
  taxAmount: 'IVA',
  totalAmount: 'Total',
  currency: 'Moneda',
  itemsDescription: 'Concepto',
};
