import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ClientCompany, clientCompaniesUrl } from './client-company';

export interface CreateClientCompanyRequest {
  name: string;
  taxId: string;
}

/** Writes. Reads use httpResource directly in the components, since they are reactive state. */
@Injectable({ providedIn: 'root' })
export class ClientCompaniesApi {
  private readonly http = inject(HttpClient);

  create(request: CreateClientCompanyRequest): Observable<ClientCompany> {
    return this.http.post<ClientCompany>(clientCompaniesUrl, request);
  }

  rename(id: string, name: string): Observable<ClientCompany> {
    return this.http.put<ClientCompany>(`${clientCompaniesUrl}/${id}`, { name });
  }

  /** Soft delete in the API: the company is archived, not removed. */
  archive(id: string): Observable<void> {
    return this.http.delete<void>(`${clientCompaniesUrl}/${id}`);
  }
}
