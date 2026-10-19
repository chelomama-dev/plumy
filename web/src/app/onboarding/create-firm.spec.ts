import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { currentUserUrl, firmsUrl } from '../core/firm/current-user';
import { CurrentUserService } from '../core/firm/current-user.service';
import { aFirm, aUser } from '../core/firm/test-data';
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
});
