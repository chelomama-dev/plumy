import { InjectionToken } from '@angular/core';
import { IPublicClientApplication, PublicClientApplication } from '@azure/msal-browser';

/**
 * Entra External ID tenant and app registrations (see infra/README.md, section "Identity").
 * They are identifiers, not secrets: a SPA can't keep secrets, it proves itself with PKCE instead.
 */
export const authConfig = {
  /** plumy-web */
  clientId: '1d98f60e-0f46-4376-a896-71780814bfdf',
  /** The tenant's sign-in domain: {subdomain}.ciamlogin.com */
  authority: 'https://plumy.ciamlogin.com/',
  /** Delegated scope of plumy-api: the access token is issued for the API only if the app asks for it */
  apiScope: 'api://c426b6f5-49cc-48fd-a005-1888c06d9542/access_as_user',
  /** Relative URLs that get the access token. Never an absolute URL: the token must not leak to other origins */
  apiPathPrefix: '/api',
} as const;

/** The MSAL client, injectable so tests can replace it. */
export const MSAL_INSTANCE = new InjectionToken<IPublicClientApplication>('MSAL_INSTANCE', {
  providedIn: 'root',
  factory: () =>
    new PublicClientApplication({
      auth: {
        clientId: authConfig.clientId,
        authority: authConfig.authority,
        // Registered in plumy-web: http://localhost:4200 and https://app.getplumy.com
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      // Tokens live only while the tab is open. Entra's session cookie signs the user back in silently,
      // so a new tab costs a redirect, not a password.
      cache: { cacheLocation: 'sessionStorage' },
    }),
});
