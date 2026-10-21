import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { MSAL_INSTANCE } from '../auth/auth.config';
import { AuthService } from '../auth/auth.service';
import { anAccount, createFakeMsal } from '../auth/fake-msal';
import { currentUserUrl } from './current-user';
import { firmGuard, noFirmGuard } from './firm.guards';
import { aUser } from './test-data';

describe('firm guards', () => {
  let msal: ReturnType<typeof createFakeMsal>;
  let http: HttpTestingController;

  beforeEach(() => {
    msal = createFakeMsal();
    TestBed.configureTestingModule({
      providers: [
        { provide: MSAL_INSTANCE, useValue: msal },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function signIn() {
    msal.getAllAccounts.mockReturnValue([anAccount()]);
    await TestBed.inject(AuthService).initialize();
  }

  const run = (guard: typeof firmGuard | typeof noFirmGuard) =>
    TestBed.runInInjectionContext(
      () =>
        guard({} as ActivatedRouteSnapshot, { url: '/' } as RouterStateSnapshot) as Promise<
          boolean | UrlTree
        >,
    );

  /** Runs the guard, answering /api/me with the given user. */
  async function runWith(guard: typeof firmGuard | typeof noFirmGuard, user = aUser()) {
    const result = run(guard);
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne(currentUserUrl).flush(user);
    const value = await result;
    return value instanceof UrlTree ? value.toString() : value;
  }

  describe('firmGuard', () => {
    it('lets users with a firm into the app', async () => {
      await signIn();

      expect(await runWith(firmGuard)).toBe(true);
    });

    it('sends users without a firm to onboarding', async () => {
      await signIn();

      expect(await runWith(firmGuard, aUser(null))).toBe('/onboarding');
    });

    it("doesn't call the API while signed out (authGuard is already signing them in)", async () => {
      expect(await run(firmGuard)).toBe(false);
      http.expectNone(currentUserUrl);
    });
  });

  describe('noFirmGuard', () => {
    it('lets users without a firm into onboarding', async () => {
      await signIn();

      expect(await runWith(noFirmGuard, aUser(null))).toBe(true);
    });

    it('sends users who already have a firm to the app', async () => {
      await signIn();

      expect(await runWith(noFirmGuard)).toBe('/');
    });
  });
});
