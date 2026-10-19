import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CurrentUser, currentUserUrl, FirmMembership, firmsUrl } from './current-user';

/**
 * Who is signed in according to Plumy (not Entra): above all, their firm. Loaded once per session from
 * /api/me; the API is still the one that enforces it on every request.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly http = inject(HttpClient);

  private readonly current = signal<CurrentUser | null>(null);
  readonly user = this.current.asReadonly();
  readonly firm = computed(() => this.current()?.firm ?? null);

  // Shared while in flight, so guards running at the same time make a single request
  private loading: Promise<CurrentUser> | null = null;

  /** The current user, from /api/me the first time and from memory afterwards. */
  load(): Promise<CurrentUser> {
    const cached = this.current();
    if (cached) {
      return Promise.resolve(cached);
    }

    this.loading ??= firstValueFrom(this.http.get<CurrentUser>(currentUserUrl))
      .then((user) => {
        this.current.set(user);
        return user;
      })
      .finally(() => (this.loading = null));
    return this.loading;
  }

  /** Onboarding: creates the user's firm and makes them its owner. */
  async createFirm(name: string): Promise<FirmMembership> {
    const firm = await firstValueFrom(this.http.post<FirmMembership>(firmsUrl, { name }));
    this.current.update((user) => (user ? { ...user, firm } : user));
    return firm;
  }

  /** Forgets the cached user, e.g. when the API says they belong to a firm we didn't know about. */
  reset(): void {
    this.current.set(null);
  }
}
