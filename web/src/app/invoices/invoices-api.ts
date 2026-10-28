import { HttpClient, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../shared/paged-result';
import {
  confirmLedgerAccountUrl,
  exportInvoicesUrl,
  ExtractedInvoiceData,
  Invoice,
  invoiceDataUrl,
  InvoiceExportParams,
  InvoiceListParams,
  invoicesUrl,
  invoiceUrl,
  retryExtractionUrl,
  ReviewStatus,
  reviewInvoiceUrl,
  uploadInvoiceUrl,
} from './invoice';

/** If-Match with the version the change was made on: the API answers 412 if the invoice changed since. */
function ifMatch(invoice: Invoice): { headers: Record<string, string> } {
  return { headers: { 'If-Match': `"${invoice.version}"` } };
}

/**
 * Writes and file downloads. The list is read with httpResource in the component. Changes take the whole invoice as
 * it was shown, for its version.
 */
@Injectable({ providedIn: 'root' })
export class InvoicesApi {
  private readonly http = inject(HttpClient);

  /** multipart/form-data: the browser sets the boundary, so no Content-Type header here. */
  upload(clientCompanyId: string, file: File): Observable<Invoice> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<Invoice>(uploadInvoiceUrl(clientCompanyId), body);
  }

  /** One invoice as it is now. For one-off reads; the review page uses httpResource. */
  get(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(invoiceUrl(id));
  }

  /** Queues the invoice to be read again. Returns it as it is now (Pending). */
  retryExtraction(id: string): Observable<Invoice> {
    return this.http.post<Invoice>(retryExtractionUrl(id), null);
  }

  /** Sets the account a person chose (the suggested one or another). Returns the invoice as it is now. */
  confirmLedgerAccount(invoice: Invoice, code: string): Observable<Invoice> {
    return this.http.put<Invoice>(confirmLedgerAccountUrl(invoice.id), { code }, ifMatch(invoice));
  }

  /** A page of invoices, newest first. For one-off reads; the list page uses httpResource. */
  list(params: InvoiceListParams): Observable<PagedResult<Invoice>> {
    return this.http.get<PagedResult<Invoice>>(invoicesUrl, { params: { ...params } });
  }

  /** Replaces what was read with the person's values (every field). The checks run again on them. */
  correctData(invoice: Invoice, data: ExtractedInvoiceData): Observable<Invoice> {
    return this.http.put<Invoice>(invoiceDataUrl(invoice.id), data, ifMatch(invoice));
  }

  /** Approves, rejects (with a reason) or reopens it (Unreviewed). */
  review(invoice: Invoice, status: ReviewStatus, rejectionReason?: string): Observable<Invoice> {
    return this.http.put<Invoice>(
      reviewInvoiceUrl(invoice.id),
      { status, rejectionReason: rejectionReason ?? null },
      ifMatch(invoice),
    );
  }

  /**
   * The approved invoices as a file. The whole response, not just the body: the file's name is in its
   * Content-Disposition header.
   */
  export(params: InvoiceExportParams): Observable<HttpResponse<Blob>> {
    // Only what was given: an empty value would be a bad request, not "any"
    const query = Object.fromEntries(Object.entries(params).filter(([, value]) => !!value));
    return this.http.get(exportInvoicesUrl, {
      params: query,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /**
   * Through HttpClient, not a plain link: the API needs the bearer token, which only the interceptor adds.
   */
  downloadFile(id: string): Observable<Blob> {
    return this.http.get(`${invoicesUrl}/${id}/file`, { responseType: 'blob' });
  }
}
