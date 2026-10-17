import { computed, inject, Injectable, signal } from '@angular/core';
import { AccountInfo, InteractionRequiredAuthError } from '@azure/msal-browser';
import { authConfig, MSAL_INSTANCE } from './auth.config';

/**
 * Sign-in with Entra External ID (authorization code flow with PKCE, full-page redirects) and access tokens
 * for the API. Wraps MSAL so the rest of the app only sees signals and promises.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MSAL_INSTANCE);
  private readonly request = { scopes: [authConfig.apiScope] };

  private readonly currentAccount = signal<AccountInfo | null>(null);
  readonly account = this.currentAccount.asReadonly();
  readonly isAuthenticated = computed(() => this.currentAccount() !== null);
  readonly userName = computed(() => {
    const account = this.currentAccount();
    return account?.name || account?.username || '';
  });

  /**
   * Runs once before the app starts (provideAuth): finishes a sign-in coming back from Entra, restores the
   * account from the cache and, after a sign-in, puts the URL the user wanted back in the address bar so the
   * router's first navigation lands there.
   */
  async initialize(): Promise<void> {
    await this.msal.initialize();
    const result = await this.msal.handleRedirectPromise({ navigateToLoginRequestUrl: false });

    const account =
      result?.account ?? this.msal.getActiveAccount() ?? this.msal.getAllAccounts()[0] ?? null;
    this.msal.setActiveAccount(account);
    this.currentAccount.set(account);

    const returnUrl = toSafeReturnUrl(result?.state);
    if (returnUrl) {
      window.history.replaceState(null, '', returnUrl);
    }
  }

  /** Redirects to the sign-in page; afterwards the user comes back to returnUrl. */
  login(returnUrl = '/'): Promise<void> {
    return this.msal.loginRedirect({ ...this.request, state: returnUrl });
  }

  logout(): Promise<void> {
    return this.msal.logoutRedirect({ account: this.currentAccount() });
  }

  /**
   * An access token for the API: from the cache, or renewed silently with the refresh token.
   * When Entra needs the user (session expired, consent...), redirects to sign in and the promise rejects.
   */
  async getAccessToken(): Promise<string> {
    const account = this.currentAccount();
    if (!account) {
      throw new Error('Not signed in.');
    }

    try {
      const result = await this.msal.acquireTokenSilent({ ...this.request, account });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await this.msal.acquireTokenRedirect({
          ...this.request,
          account,
          state: window.location.pathname + window.location.search,
        });
      }
      throw error;
    }
  }
}

/**
 * The state comes back in the URL, so anyone can craft it: only same-site paths are accepted,
 * never "//evil.com" or "https://evil.com" (open redirect).
 */
export function toSafeReturnUrl(state: string | undefined): string | null {
  if (!state || !state.startsWith('/') || state.startsWith('//') || state.startsWith('/\\')) {
    return null;
  }
  return state;
}
