import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { MSAL_INSTANCE } from './core/auth/auth.config';
import { AuthService } from './core/auth/auth.service';
import { anAccount, createFakeMsal } from './core/auth/fake-msal';

describe('App', () => {
  let msal: ReturnType<typeof createFakeMsal>;

  beforeEach(async () => {
    msal = createFakeMsal();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: MSAL_INSTANCE, useValue: msal }],
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
});
