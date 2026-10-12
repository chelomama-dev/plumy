import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ClientCompany, clientCompaniesUrl } from '../client-company';
import { ClientCompanyFormDialog, ClientCompanyFormDialogData } from './client-company-form-dialog';

describe('ClientCompanyFormDialog', () => {
  let fixture: ComponentFixture<ClientCompanyFormDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const acme: ClientCompany = {
    id: 'acme-id',
    name: 'Acme S.L.',
    taxId: 'B12345674',
    taxIdType: 'Cif',
  };

  async function open(data: ClientCompanyFormDialogData = {}) {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [ClientCompanyFormDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(ClientCompanyFormDialog);
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const inputs = () => element().querySelectorAll<HTMLInputElement>('input');
  const nameInput = () => inputs()[0];
  const taxIdInput = () => inputs()[1];
  const errors = () =>
    Array.from(element().querySelectorAll('mat-error, .form-error')).map((e) =>
      e.textContent?.trim(),
    );

  async function type(input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  /** Submits through the <form>, like the button does. Not awaited: the save may be waiting on HTTP. */
  function submit() {
    element()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
  }

  /** Lets the submit action resume after the HTTP response, then renders. */
  async function settle() {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  describe('create', () => {
    beforeEach(() => open());

    it('shows the required errors and sends nothing when submitted empty', async () => {
      submit();
      await fixture.whenStable();

      expect(errors()).toEqual(['El nombre es obligatorio.', 'El NIF es obligatorio.']);
      http.expectNone(clientCompaniesUrl);
    });

    it('rejects a whitespace-only name and an invalid tax ID', async () => {
      await type(nameInput(), '   ');
      await type(taxIdInput(), '12345678A');

      expect(errors()).toEqual(['El nombre es obligatorio.', 'No es un NIF, NIE o CIF válido.']);
    });

    it('rejects a name longer than the domain allows', async () => {
      await type(nameInput(), 'x'.repeat(201));

      expect(errors()).toEqual(['El nombre no puede superar los 200 caracteres.']);
    });

    it('posts the company and closes with the result', async () => {
      await type(nameInput(), 'Acme S.L.');
      await type(taxIdInput(), 'b-1234567-4');
      submit();

      const request = http.expectOne(clientCompaniesUrl);
      expect(request.request.method).toBe('POST');
      // The API normalizes the tax ID, so it is sent as typed
      expect(request.request.body).toEqual({ name: 'Acme S.L.', taxId: 'b-1234567-4' });
      request.flush(acme);
      await settle();

      expect(close).toHaveBeenCalledWith(acme);
    });

    it('shows a 409 on the tax ID field and stays open', async () => {
      await type(nameInput(), 'Acme S.L.');
      await type(taxIdInput(), 'B12345674');
      submit();

      http
        .expectOne(clientCompaniesUrl)
        .flush({ detail: 'already exists' }, { status: 409, statusText: 'Conflict' });
      await settle();

      // On the tax ID field itself, not as a form-level error
      expect(
        taxIdInput().closest('mat-form-field')!.querySelector('mat-error')?.textContent,
      ).toContain('Ya existe una empresa cliente con este NIF (puede que esté archivada).');
      expect(element().querySelector('.form-error')).toBeNull();
      expect(close).not.toHaveBeenCalled();
    });

    it('shows a form-level error when the server fails', async () => {
      await type(nameInput(), 'Acme S.L.');
      await type(taxIdInput(), 'B12345674');
      submit();

      http.expectOne(clientCompaniesUrl).flush(null, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(errors()).toEqual(['No se ha podido guardar. Inténtalo de nuevo.']);
      expect(close).not.toHaveBeenCalled();
    });
  });

  describe('rename', () => {
    beforeEach(() => open({ company: acme }));

    it('starts with the current values and the tax ID locked', () => {
      expect(nameInput().value).toBe('Acme S.L.');
      expect(taxIdInput().value).toBe('B12345674');
      expect(taxIdInput().disabled).toBe(true);
    });

    it('puts only the new name and closes with the result', async () => {
      await type(nameInput(), 'Acme Global S.L.');
      submit();

      const request = http.expectOne(`${clientCompaniesUrl}/acme-id`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ name: 'Acme Global S.L.' });
      const renamed = { ...acme, name: 'Acme Global S.L.' };
      request.flush(renamed);
      await settle();

      expect(close).toHaveBeenCalledWith(renamed);
    });

    it('says so when the company no longer exists', async () => {
      submit();

      http
        .expectOne(`${clientCompaniesUrl}/acme-id`)
        .flush(null, { status: 404, statusText: 'Not Found' });
      await settle();

      expect(errors()).toEqual(['Esta empresa ya no existe o se ha archivado.']);
    });
  });
});
