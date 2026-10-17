import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { MSAL_INSTANCE } from './auth.config';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { anAccount, createFakeMsal } from './fake-msal';

describe('authGuard', () => {
  let msal: ReturnType<typeof createFakeMsal>;

  beforeEach(() => {
    msal = createFakeMsal();
    TestBed.configureTestingModule({ providers: [{ provide: MSAL_INSTANCE, useValue: msal }] });
  });

  const run = (url: string) =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );

  it('lets signed-in users through', async () => {
    msal.getAllAccounts.mockReturnValue([anAccount()]);
    await TestBed.inject(AuthService).initialize();

    expect(run('/client-companies')).toBe(true);
    expect(msal.loginRedirect).not.toHaveBeenCalled();
  });

  it('sends everyone else to sign in, coming back to the same URL', () => {
    expect(run('/client-companies?page=2')).toBe(false);
    expect(msal.loginRedirect).toHaveBeenCalledWith(
      expect.objectContaining({ state: '/client-companies?page=2' }),
    );
  });
});
