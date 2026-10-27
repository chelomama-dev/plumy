import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { MSAL_INSTANCE } from './core/auth/auth.config';
import { AuthService } from './core/auth/auth.service';
import { anAccount, createFakeMsal } from './core/auth/fake-msal';
import { currentUserUrl, FirmMembership } from './core/firm/current-user';
import { CurrentUserService } from './core/firm/current-user.service';
import { aFirm, aUser } from './core/firm/test-data';
import { Invoice } from './invoices/invoice';
import { INVOICE_ACTIVITY_INTERVAL, InvoiceActivity } from './invoices/invoice-activity';

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
        // Followed invoices are never checked within these tests
        { provide: INVOICE_ACTIVITY_INTERVAL, useValue: 60 * 60 * 1000 },
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

  it("shows while the AI works on this person's invoices, and what it is doing", async () => {
    msal.getAllAccounts.mockReturnValue([anAccount()]);
    await TestBed.inject(AuthService).initialize();
    const page = await render();
    expect(page.querySelector('.ai-activity')).toBeNull();

    const now = new Date().toISOString();
    const pending = (n: number, status: Invoice['extractionStatus']) =>
      ({
        id: `id-${n}`,
        fileName: `factura-${n}.pdf`,
        extractionStatus: status,
        extractionQueuedAt: now,
        uploadedAt: now,
        reviewStatus: 'Unreviewed',
      }) as Invoice;
    TestBed.inject(InvoiceActivity).watch([pending(1, 'Processing'), pending(2, 'Pending')]);
    await TestBed.inject(ApplicationRef).whenStable();

    const pill = page.querySelector<HTMLAnchorElement>('.ai-activity');
    expect(pill?.textContent).toContain('La IA está con 2 facturas');
    expect(pill?.getAttribute('href')).toBe('/invoices');
    expect(pill?.getAttribute('role')).toBe('status');
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
