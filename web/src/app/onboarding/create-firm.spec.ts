import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { acceptInvitationUrl, currentUserUrl, firmsUrl } from '../core/firm/current-user';
import { CurrentUserService } from '../core/firm/current-user.service';
import { aFirm, anInvitation, aUser } from '../core/firm/test-data';
import { CreateFirm } from './create-firm';

describe('CreateFirm', () => {
  let fixture: ComponentFixture<CreateFirm>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [CreateFirm],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    // As after noFirmGuard: the user is loaded and has no firm
    const loaded = TestBed.inject(CurrentUserService).load();
    http.expectOne(currentUserUrl).flush(aUser(null));
    await loaded;

    fixture = TestBed.createComponent(CreateFirm);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const nameInput = () => element().querySelector<HTMLInputElement>('input')!;
  const errors = () =>
    Array.from(element().querySelectorAll('mat-error, .form-error')).map((e) =>
      e.textContent?.trim(),
    );

  async function type(value: string) {
    nameInput().value = value;
    nameInput().dispatchEvent(new Event('input'));
    nameInput().dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  function submit() {
    element()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
  }

  async function settle() {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  it('greets the user by name', () => {
    expect(element().textContent).toContain('Te damos la bienvenida a Plumy, Ana García');
  });

  it('shows no invitations section when nobody invited the user', () => {
    expect(element().textContent).not.toContain('Te han invitado');
  });

  it('requires a name and sends nothing without one', async () => {
    await type('   ');
    submit();
    await fixture.whenStable();

    expect(errors()).toEqual(['El nombre es obligatorio.']);
    http.expectNone(firmsUrl);
  });

  it('rejects a name longer than the domain allows', async () => {
    await type('x'.repeat(201));

    expect(errors()).toEqual(['El nombre no puede superar los 200 caracteres.']);
  });

  it('creates the firm and goes into the app', async () => {
    await type('Gestoría López');
    submit();

    const request = http.expectOne(firmsUrl);
    expect(request.request.body).toEqual({ name: 'Gestoría López' });
    request.flush(aFirm);
    await settle();

    expect(TestBed.inject(CurrentUserService).firm()).toEqual(aFirm);
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('goes into the app when the user turns out to have a firm already (409)', async () => {
    await type('Gestoría López');
    submit();

    http.expectOne(firmsUrl).flush(null, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(navigate).toHaveBeenCalledWith('/');
    expect(errors()).toEqual([]);
  });

  it('shows an error and stays when the server fails', async () => {
    await type('Gestoría López');
    submit();

    http.expectOne(firmsUrl).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(errors()).toEqual(['No se ha podido crear la gestoría. Inténtalo de nuevo.']);
    expect(navigate).not.toHaveBeenCalled();
  });

  describe('with invitations', () => {
    const member = { ...aFirm, name: anInvitation.firmName, role: 'Member' as const };

    beforeEach(async () => {
      const currentUser = TestBed.inject(CurrentUserService);
      currentUser.reset();
      const loaded = currentUser.load();
      http.expectOne(currentUserUrl).flush(aUser(null, [anInvitation]));
      await loaded;
      await fixture.whenStable();
    });

    const joinButton = () =>
      element().querySelector<HTMLButtonElement>(
        `button[aria-label="Unirme a ${anInvitation.firmName}"]`,
      );

    it('lists the firms that invited the user, and still lets them create their own', () => {
      expect(element().textContent).toContain('Te han invitado');
      expect(element().querySelector('.invitations')?.textContent).toContain('Asesoría Martínez');
      expect(nameInput()).not.toBeNull();
    });

    it('joins the firm and goes into the app', async () => {
      joinButton()!.click();

      const request = http.expectOne(acceptInvitationUrl(anInvitation.id));
      expect(request.request.method).toBe('POST');
      request.flush(member);
      await settle();

      expect(TestBed.inject(CurrentUserService).firm()).toEqual(member);
      expect(navigate).toHaveBeenCalledWith('/');
    });

    it('disables the buttons while joining', async () => {
      joinButton()!.click();
      await fixture.whenStable();

      expect(joinButton()!.disabled).toBe(true);

      http.expectOne(acceptInvitationUrl(anInvitation.id)).flush(member);
      await settle();
    });

    it('explains a revoked invitation (404) and refreshes the list', async () => {
      joinButton()!.click();

      http
        .expectOne(acceptInvitationUrl(anInvitation.id))
        .flush(null, { status: 404, statusText: 'Not Found' });
      await settle();
      http.expectOne(currentUserUrl).flush(aUser(null, []));
      await settle();

      expect(errors()).toEqual([
        'La invitación ya no está disponible: puede que la gestoría la haya anulado.',
      ]);
      expect(element().textContent).not.toContain('Te han invitado');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('explains an expired invitation (400)', async () => {
      joinButton()!.click();

      http
        .expectOne(acceptInvitationUrl(anInvitation.id))
        .flush(null, { status: 400, statusText: 'Bad Request' });
      await settle();
      http.expectOne(currentUserUrl).flush(aUser(null, []));
      await settle();

      expect(errors()).toEqual([
        'La invitación ha caducado. Pide a la gestoría que te invite de nuevo.',
      ]);
    });

    it('goes into the app when the user already has a firm (409)', async () => {
      joinButton()!.click();

      http
        .expectOne(acceptInvitationUrl(anInvitation.id))
        .flush(null, { status: 409, statusText: 'Conflict' });
      await settle();

      expect(navigate).toHaveBeenCalledWith('/');
    });

    it('keeps the invitation on screen when the server fails', async () => {
      joinButton()!.click();

      http
        .expectOne(acceptInvitationUrl(anInvitation.id))
        .flush(null, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(errors()).toEqual(['No se ha podido aceptar la invitación. Inténtalo de nuevo.']);
      expect(joinButton()!.disabled).toBe(false);
    });
  });
});
