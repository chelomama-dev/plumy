import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { PagedResult } from '../../shared/paged-result';
import { Invoice, invoicesUrl } from '../invoice';
import { UploadInvoicesDialog } from '../upload-invoices-dialog/upload-invoices-dialog';
import { InvoiceList } from './invoice-list';

describe('InvoiceList', () => {
  let fixture: ComponentFixture<InvoiceList>;
  let http: HttpTestingController;
  let dialogResult: number | undefined;
  let openDialog: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    dialogResult = undefined;
    openDialog = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    TestBed.configureTestingModule({
      imports: [InvoiceList],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: openDialog } },
      ],
    });
    fixture = TestBed.createComponent(InvoiceList);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  const invoice = (n: number, contentType = 'application/pdf'): Invoice => ({
    id: `id-${n}`,
    clientCompanyId: 'acme-id',
    clientCompanyName: 'Acme S.L.',
    fileName: `factura-${n}.pdf`,
    contentType,
    sizeBytes: 250_880,
    uploadedAt: '2026-10-22T09:05:00',
  });

  const pageOf = (items: Invoice[], totalCount = items.length): PagedResult<Invoice> => ({
    items,
    page: 1,
    pageSize: 20,
    totalCount,
    totalPages: Math.ceil(totalCount / 20),
  });

  async function respond(body: PagedResult<Invoice>) {
    TestBed.tick();
    const request = http.expectOne((r) => r.url === invoicesUrl);
    request.flush(body);
    await fixture.whenStable();
    return request.request.params;
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(element().querySelectorAll<HTMLElement>('tr.mat-mdc-row'));

  it('loads the first page and shows file, company, date and size', async () => {
    const params = await respond(pageOf([invoice(1)]));

    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
    const cells = Array.from(rows()[0].querySelectorAll('td')).map((td) => td.textContent?.trim());
    expect(cells).toEqual([
      expect.stringContaining('factura-1.pdf'),
      'Acme S.L.',
      '22/10/2026 09:05',
      '245 KB',
    ]);
  });

  it('says so when there are no invoices yet', async () => {
    await respond(pageOf([]));

    expect(element().textContent).toContain('Todavía no hay facturas.');
  });

  it('shows an error with a retry button when the list fails', async () => {
    http.expectOne((r) => r.url === invoicesUrl).flush(null, { status: 500, statusText: 'Error' });
    await fixture.whenStable();

    expect(element().textContent).toContain('No se han podido cargar las facturas.');
    element().querySelector<HTMLButtonElement>('.error button')!.click();
    await respond(pageOf([invoice(1)]));
    expect(rows()).toHaveLength(1);
  });

  it('opens the upload dialog and reloads the list when something was uploaded', async () => {
    await respond(pageOf([invoice(1)]));
    dialogResult = 2;

    element().querySelector<HTMLButtonElement>('header button')!.click();
    await new Promise((resolve) => setTimeout(resolve));

    expect(openDialog).toHaveBeenCalledWith(
      UploadInvoicesDialog,
      expect.objectContaining({ disableClose: true }),
    );
    const params = await respond(pageOf([invoice(3), invoice(2), invoice(1)]));
    expect(params.get('page')).toBe('1');
    expect(rows()).toHaveLength(3);
  });

  it('does not reload when the dialog closes without uploads', async () => {
    await respond(pageOf([invoice(1)]));
    dialogResult = 0;

    element().querySelector<HTMLButtonElement>('header button')!.click();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    http.expectNone((r) => r.url === invoicesUrl);
  });

  it('downloads the file through the API with its original name', async () => {
    await respond(pageOf([invoice(1)]));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    URL.createObjectURL = vi.fn(() => 'blob:factura');
    URL.revokeObjectURL = vi.fn();

    rows()[0].querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne(`${invoicesUrl}/id-1/file`).flush(new Blob(['%PDF-']));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));

    const link = click.mock.contexts[0] as HTMLAnchorElement;
    expect(link.download).toBe('factura-1.pdf');
    expect(link.href).toBe('blob:factura');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:factura');
    click.mockRestore();
  });
});
