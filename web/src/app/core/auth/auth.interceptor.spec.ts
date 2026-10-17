import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { MSAL_INSTANCE } from './auth.config';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { aRedirectResult, anAccount, createFakeMsal } from './fake-msal';

describe('authInterceptor', () => {
  let msal: ReturnType<typeof createFakeMsal>;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    msal = createFakeMsal();
    msal.acquireTokenSilent.mockResolvedValue(aRedirectResult({ accessToken: 'api-token' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: MSAL_INSTANCE, useValue: msal },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function signIn() {
    msal.getAllAccounts.mockReturnValue([anAccount()]);
    await TestBed.inject(AuthService).initialize();
  }

  /** Sends a GET and returns the request as it reached the backend (after the token is resolved). */
  async function send(url: string) {
    const response = firstValueFrom(http.get(url));
    // The token comes from a promise: let it resolve before the request reaches the backend
    await new Promise((resolve) => setTimeout(resolve));
    const request = backend.expectOne(url);
    request.flush({});
    await response;
    return request.request;
  }

  it('adds the access token to API calls', async () => {
    await signIn();

    const request = await send('/api/client-companies?page=1');

    expect(request.headers.get('Authorization')).toBe('Bearer api-token');
  });

  it.each(['https://evil.example/api/client-companies', '/assets/i18n.json', '/apiary'])(
    'never sends the token outside the API: %s',
    async (url) => {
      await signIn();

      const request = await send(url);

      expect(request.headers.has('Authorization')).toBe(false);
      expect(msal.acquireTokenSilent).not.toHaveBeenCalled();
    },
  );

  it('sends API calls without a token when signed out (the API answers 401)', async () => {
    const request = await send('/api/client-companies');

    expect(request.headers.has('Authorization')).toBe(false);
  });
});
