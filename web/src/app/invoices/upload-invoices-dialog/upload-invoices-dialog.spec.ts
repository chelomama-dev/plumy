import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { ClientCompany, clientCompaniesUrl } from '../../client-companies/client-company';
import { PagedResult } from '../../shared/paged-result';
import { Invoice, uploadInvoiceUrl } from '../invoice';
import { companySearchDebounceMs } from '../../client-companies/client-company-picker/client-company-picker';
import { UploadInvoicesDialog, uploadErrorMessage } from './upload-invoices-dialog';

describe('UploadInvoicesDialog', () => {
  let fixture: ComponentFixture<UploadInvoicesDialog>;
  let loader: HarnessLoader;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const acme: ClientCompany = {
    id: 'acme-id',
    name: 'Acme S.L.',
    taxId: 'B12345674',
    taxIdType: 'Cif',
  };

  const companies = (items: ClientCompany[]): PagedResult<ClientCompany> => ({
    items,
    page: 1,
    pageSize: 10,
    totalCount: items.length,
    totalPages: 1,
  });

  const invoice = (fileName: string): Invoice => ({
    id: `id-${fileName}`,
    clientCompanyId: acme.id,
    clientCompanyName: acme.name,
    fileName,
    contentType: 'application/pdf',
    sizeBytes: 1000,
    uploadedAt: '2026-10-22T09:00:00+00:00',
    extractionQueuedAt: '2026-10-22T09:00:00+00:00',
    suggestionStatus: null,
    noSuggestionReason: null,
    extractionStatus: 'Pending',
    extractionError: null,
    extractedAt: null,
    extractedData: null,
    ledgerAccountSuggestion: null,
    ledgerAccount: null,
    issues: [],
    duplicateOfInvoiceId: null,
    fieldConfidence: {},
    correctedFields: [],
    reviewStatus: 'Unreviewed',
    approvedAutomatically: false,
    reviewedAt: null,
    rejectionReason: null,
    version: '00000000000007D1',
  });

  beforeEach(() => {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [UploadInvoicesDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(UploadInvoicesDialog);
    loader = TestbedHarnessEnvironment.loader(fixture);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent ?? '';
  const buttons = () =>
    Array.from(element().querySelectorAll<HTMLButtonElement>('mat-dialog-actions button'));
  const uploadButton = () => buttons()[1];
  const closeButton = () => buttons()[0];

  /** Answers the company search that is pending, if any. */
  async function respondCompanies(items: ClientCompany[]) {
    TestBed.tick();
    const request = http.expectOne((r) => r.url === clientCompaniesUrl);
    request.flush(companies(items));
    await fixture.whenStable();
    return request.request.params;
  }

  async function chooseAcme() {
    await respondCompanies([acme]);
    const autocomplete = await loader.getHarness(MatAutocompleteHarness);
    await autocomplete.enterText('acm');
    await new Promise((resolve) => setTimeout(resolve, companySearchDebounceMs + 50));
    const params = await respondCompanies([acme]);
    await autocomplete.selectOption({ text: /Acme S\.L\./ });
    return params;
  }

  /** Same as dropping files on the drop zone. */
  async function drop(...files: File[]) {
    const event = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: { files } });
    element().querySelector('.drop-zone')!.dispatchEvent(event);
    await fixture.whenStable();
  }

  const pdf = (name: string) => new File(['%PDF-1.7'], name, { type: 'application/pdf' });

  const fileRows = () =>
    Array.from(element().querySelectorAll<HTMLElement>('.files li')).map((li) => ({
      status: li.className,
      text: li.textContent?.replace(/\s+/g, ' ').trim(),
    }));

  it('searches the companies by what is typed', async () => {
    const params = await chooseAcme();

    expect(params.get('search')).toBe('acm');
    expect(params.get('pageSize')).toBe('10');
  });

  it('only lets you upload once there is a company and a valid file', async () => {
    await respondCompanies([acme]);
    expect(uploadButton().disabled).toBe(true);

    await drop(pdf('factura.pdf'));
    expect(uploadButton().disabled).toBe(true);

    const autocomplete = await loader.getHarness(MatAutocompleteHarness);
    await autocomplete.selectOption({ text: /Acme/ });
    expect(uploadButton().disabled).toBe(false);
  });

  it('flags files it can already tell are wrong, without sending them', async () => {
    await chooseAcme();

    await drop(new File(['hola'], 'notas.txt'));

    expect(fileRows()).toEqual([
      { status: 'error', text: expect.stringContaining('Solo se aceptan PDF, JPG y PNG.') },
    ]);
    expect(uploadButton().disabled).toBe(true);
  });

  it('uploads each file on its own and shows how each one went', async () => {
    await chooseAcme();
    await drop(pdf('una.pdf'), pdf('repetida.pdf'));
    expect(uploadButton().textContent).toContain('Subir 2 facturas');

    uploadButton().click();
    await fixture.whenStable();

    // One at a time: the second waits for the first
    const first = http.expectOne(uploadInvoiceUrl(acme.id));
    expect((first.request.body as FormData).get('file')).toBeInstanceOf(File);
    expect(closeButton().disabled).toBe(true);
    first.flush(invoice('una.pdf'));
    await fixture.whenStable();

    http.expectOne(uploadInvoiceUrl(acme.id)).flush(null, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(fileRows()).toEqual([
      { status: 'done', text: expect.stringContaining('una.pdf') },
      {
        status: 'error',
        text: expect.stringContaining('Ya se había subido este archivo para esta empresa.'),
      },
    ]);

    expect(closeButton().textContent).toContain('Cerrar');
    closeButton().click();
    expect(close).toHaveBeenCalledWith([expect.objectContaining({ fileName: 'una.pdf' })]);
  });

  it('closes with no invoices when nothing was uploaded', async () => {
    await respondCompanies([acme]);

    closeButton().click();

    expect(text()).toContain('Cancelar');
    expect(close).toHaveBeenCalledWith([]);
  });

  it('lets you remove a file before uploading', async () => {
    await respondCompanies([acme]);
    await drop(pdf('una.pdf'), pdf('otra.pdf'));

    element().querySelector<HTMLButtonElement>('[aria-label="Quitar una.pdf"]')!.click();
    await fixture.whenStable();

    expect(fileRows().map((r) => r.text)).toEqual([expect.stringContaining('otra.pdf')]);
  });
});

describe('uploadErrorMessage', () => {
  it('sends the user to the plan when the plan stops the upload', () => {
    expect(uploadErrorMessage(new HttpErrorResponse({ status: 402 }))).toBe(
      'El plan no permite subir más facturas ahora. Revísalo en «Plan».',
    );
  });
});
