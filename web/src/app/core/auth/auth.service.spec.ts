import { TestBed } from '@angular/core/testing';
import { InteractionRequiredAuthError } from '@azure/msal-browser';
import { MSAL_INSTANCE, authConfig } from './auth.config';
import { AuthService, toSafeReturnUrl } from './auth.service';
import { aRedirectResult, anAccount, createFakeMsal } from './fake-msal';

describe('AuthService', () => {
  let msal: ReturnType<typeof createFakeMsal>;
  let auth: AuthService;

  beforeEach(() => {
    msal = createFakeMsal();
    TestBed.configureTestingModule({ providers: [{ provide: MSAL_INSTANCE, useValue: msal }] });
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => window.history.replaceState(null, '', '/'));

  describe('initialize', () => {
    it('is signed out when there is no redirect result and no cached account', async () => {
      await auth.initialize();

      expect(msal.initialize).toHaveBeenCalled();
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.userName()).toBe('');
    });

    it('signs in with the account coming back from Entra', async () => {
      msal.handleRedirectPromise.mockResolvedValue(
        aRedirectResult({ account: anAccount({ name: 'Ana' }) }),
      );

      await auth.initialize();

      expect(auth.isAuthenticated()).toBe(true);
      expect(auth.userName()).toBe('Ana');
      expect(msal.setActiveAccount).toHaveBeenCalledWith(expect.objectContaining({ name: 'Ana' }));
    });

    it('restores the account from the cache after a reload', async () => {
      msal.getAllAccounts.mockReturnValue([anAccount({ name: '', username: 'ana@gestoria.es' })]);

      await auth.initialize();

      expect(auth.isAuthenticated()).toBe(true);
      expect(auth.userName()).toBe('ana@gestoria.es');
    });

    it('handles the redirect itself instead of letting MSAL reload the page', async () => {
      await auth.initialize();

      expect(msal.handleRedirectPromise).toHaveBeenCalledWith({ navigateToLoginRequestUrl: false });
    });

    it('goes back to the page the user wanted before signing in', async () => {
      msal.handleRedirectPromise.mockResolvedValue(
        aRedirectResult({ state: '/client-companies?page=2' }),
      );

      await auth.initialize();

      expect(window.location.pathname + window.location.search).toBe('/client-companies?page=2');
    });

    it('ignores a return URL to another site', async () => {
      msal.handleRedirectPromise.mockResolvedValue(aRedirectResult({ state: '//evil.example' }));

      await auth.initialize();

      expect(window.location.pathname).toBe('/');
    });
  });

  it('login redirects to Entra asking for the API scope and remembering the return URL', async () => {
    await auth.login('/client-companies');

    expect(msal.loginRedirect).toHaveBeenCalledWith({
      scopes: [authConfig.apiScope],
      state: '/client-companies',
    });
  });

  describe('getAccessToken', () => {
    beforeEach(async () => {
      msal.getAllAccounts.mockReturnValue([anAccount()]);
      await auth.initialize();
    });

    it('returns the token for the API from the cache or a silent renewal', async () => {
      msal.acquireTokenSilent.mockResolvedValue(aRedirectResult({ accessToken: 'api-token' }));

      await expect(auth.getAccessToken()).resolves.toBe('api-token');
      expect(msal.acquireTokenSilent).toHaveBeenCalledWith(
        expect.objectContaining({ scopes: [authConfig.apiScope] }),
      );
    });

    it('redirects to sign in when Entra needs the user', async () => {
      const error = new InteractionRequiredAuthError('login_required', 'test-correlation-id');
      msal.acquireTokenSilent.mockRejectedValue(error);

      await expect(auth.getAccessToken()).rejects.toBe(error);
      expect(msal.acquireTokenRedirect).toHaveBeenCalled();
    });

    it('does not redirect on other errors (e.g. network)', async () => {
      msal.acquireTokenSilent.mockRejectedValue(new Error('network'));

      await expect(auth.getAccessToken()).rejects.toThrow('network');
      expect(msal.acquireTokenRedirect).not.toHaveBeenCalled();
    });
  });
});

describe('toSafeReturnUrl', () => {
  it.each([
    ['/client-companies', '/client-companies'],
    ['/client-companies?search=acme', '/client-companies?search=acme'],
    [undefined, null],
    ['', null],
    ['client-companies', null],
    ['//evil.example', null],
    ['/\\evil.example', null],
    ['https://evil.example', null],
  ])('%s → %s', (state, expected) => {
    expect(toSafeReturnUrl(state)).toBe(expected);
  });
});
