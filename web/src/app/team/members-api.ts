import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { membersUrl } from './member';

/** Writes. The list is read with httpResource in the component. */
@Injectable({ providedIn: 'root' })
export class MembersApi {
  private readonly http = inject(HttpClient);

  /** Owner only. The owner themselves can't be removed. */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${membersUrl}/${id}`);
  }
}
