import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { MSAL_INSTANCE } from './core/auth/auth.config';
import { AuthService } from './core/auth/auth.service';
import { anAccount, createFakeMsal } from './core/auth/fake-msal';
import { currentUserUrl, FirmMembership } from './core/firm/current-user';
import { CurrentUserService } from './core/firm/current-user.service';
import { aFirm, aUser } from './core/firm/test-data';

describe('App', () => {
  let msal: ReturnType<typeof createFakeMsal>;

  beforeEach(async () => {
    msal = createFakeMsal();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MSAL_INSTANCE, useValue: msal },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('should render the brand in the toolbar', async () => {
    const page = await render();

    expect(page.querySelector('mat-toolbar')?.textContent).toContain('Plumy');
  });

  it('shows the signed-in user and lets them sign out', async () => {
    msal.getAllAccounts.mockReturnValue([anAccount({ name: 'Ana García' })]);
    await TestBed.inject(AuthService).initialize();

    const page = await render();
    const signOut = Array.from(page.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Cerrar sesión'),
    );
    signOut?.click();

    expect(page.querySelector('.user-name')?.textContent).toContain('Ana García');
    expect(msal.logoutRedirect).toHaveBeenCalled();
  });

  it('shows no user menu when signed out', async () => {
    const page = await render();

    expect(page.querySelector('.user-name')).toBeNull();
  });

  describe('team link', () => {
    async function renderSignedInWith(firm: FirmMembership) {
      msal.getAllAccounts.mockReturnValue([anAccount()]);
      await TestBed.inject(AuthService).initialize();
      const loaded = TestBed.inject(CurrentUserService).load();
      TestBed.inject(HttpTestingController).expectOne(currentUserUrl).flush(aUser(firm));
      await loaded;
      return render();
    }

    const teamLink = (page: HTMLElement) =>
      page.querySelector<HTMLAnchorElement>('a[href="/team"]');

    it("is shown to the firm's owner", async () => {
      const page = await renderSignedInWith(aFirm);

      expect(teamLink(page)?.textContent).toContain('Equipo');
    });

    it('is shown to members too: they can see who is in the firm', async () => {
      const page = await renderSignedInWith({ ...aFirm, role: 'Member' });

      expect(teamLink(page)?.textContent).toContain('Equipo');
      expect(page.querySelector('.firm-name')?.textContent).toContain(aFirm.name);
    });
  });
});
