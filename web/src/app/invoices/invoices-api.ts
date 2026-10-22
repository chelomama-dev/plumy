import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Invoice, invoicesUrl, uploadInvoiceUrl } from './invoice';

/** Writes and file downloads. The list is read with httpResource in the component. */
@Injectable({ providedIn: 'root' })
export class InvoicesApi {
  private readonly http = inject(HttpClient);

  /** multipart/form-data: the browser sets the boundary, so no Content-Type header here. */
  upload(clientCompanyId: string, file: File): Observable<Invoice> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<Invoice>(uploadInvoiceUrl(clientCompanyId), body);
  }

  /**
   * Through HttpClient, not a plain link: the API needs the bearer token, which only the interceptor adds.
   */
  downloadFile(id: string): Observable<Blob> {
    return this.http.get(`${invoicesUrl}/${id}/file`, { responseType: 'blob' });
  }
}
