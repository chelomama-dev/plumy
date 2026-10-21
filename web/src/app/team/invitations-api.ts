import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Invitation, invitationsUrl } from './invitation';

/** Writes. The list is read with httpResource in the component. */
@Injectable({ providedIn: 'root' })
export class InvitationsApi {
  private readonly http = inject(HttpClient);

  /** Invites the address, or renews its open invitation if it already has one. */
  invite(email: string): Observable<Invitation> {
    return this.http.post<Invitation>(invitationsUrl, { email });
  }

  revoke(id: string): Observable<void> {
    return this.http.delete<void>(`${invitationsUrl}/${id}`);
  }
}
