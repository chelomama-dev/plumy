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
}

export const invoicesUrl = '/api/invoices';

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
