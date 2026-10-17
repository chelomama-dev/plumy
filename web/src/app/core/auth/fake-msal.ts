import { AccountInfo, AuthenticationResult, IPublicClientApplication } from '@azure/msal-browser';
import { vi } from 'vitest';

/** An MSAL client that never leaves the page: each test sets up only what it needs. */
export function createFakeMsal() {
  const fake = {
    initialize: vi.fn().mockResolvedValue(undefined),
    handleRedirectPromise: vi.fn().mockResolvedValue(null),
    getActiveAccount: vi.fn().mockReturnValue(null),
    getAllAccounts: vi.fn().mockReturnValue([]),
    setActiveAccount: vi.fn(),
    loginRedirect: vi.fn().mockResolvedValue(undefined),
    logoutRedirect: vi.fn().mockResolvedValue(undefined),
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn().mockResolvedValue(undefined),
  };
  return fake as typeof fake & IPublicClientApplication;
}

export function anAccount(overrides: Partial<AccountInfo> = {}): AccountInfo {
  return {
    homeAccountId: 'home-id',
    environment: 'plumy.ciamlogin.com',
    tenantId: 'tenant-id',
    username: 'ana@gestoria.es',
    localAccountId: 'local-id',
    name: 'Ana García',
    ...overrides,
  };
}

export function aRedirectResult(overrides: Partial<AuthenticationResult>): AuthenticationResult {
  return {
    account: anAccount(),
    state: undefined,
    accessToken: 'token',
    ...overrides,
  } as AuthenticationResult;
}
