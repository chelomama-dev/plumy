import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { PagedResult } from '../../shared/paged-result';
import {
  confirmLedgerAccountUrl,
  Invoice,
  invoicesUrl,
  LedgerAccount,
  ledgerAccountsUrl,
  retryExtractionUrl,
} from '../invoice';
import { UploadInvoicesDialog } from '../upload-invoices-dialog/upload-invoices-dialog';
import { INVOICE_REFRESH_INTERVAL, InvoiceList } from './invoice-list';

describe('InvoiceList', () => {
  const refreshInterval = 20;
  const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
  let fixture: ComponentFixture<InvoiceList>;
  let http: HttpTestingController;
  let dialogResult: number | undefined;
  let openDialog: ReturnType<typeof vi.fn>;
  let openSnackBar: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    dialogResult = undefined;
    openDialog = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    openSnackBar = vi.fn();
    TestBed.configureTestingModule({
      imports: [InvoiceList],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: openDialog } },
        { provide: MatSnackBar, useValue: { open: openSnackBar } },
        { provide: INVOICE_REFRESH_INTERVAL, useValue: refreshInterval },
      ],
    });
    fixture = TestBed.createComponent(InvoiceList);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
    // The chart of accounts, read once when the list opens. Answered here: a pending request keeps the
    // fixture from ever being stable
    http.expectOne(ledgerAccountsUrl).flush(accounts);
  });

  afterEach(() => http.verify());

  const invoice = (n: number, overrides: Partial<Invoice> = {}): Invoice => ({
    id: `id-${n}`,
    clientCompanyId: 'acme-id',
    clientCompanyName: 'Acme S.L.',
    fileName: `factura-${n}.pdf`,
    contentType: 'application/pdf',
    sizeBytes: 250_880,
    uploadedAt: '2026-10-22T09:05:00',
    extractionStatus: 'Extracted',
    extractionError: null,
    extractedAt: '2026-10-22T09:05:03',
    extractedData: {
      supplierName: 'Papelería Sol S.L.',
      supplierTaxId: 'B12345674',
      invoiceNumber: 'F-42',
      issueDate: '2026-10-01',
      netAmount: 1234.56,
      taxAmount: 259.26,
      totalAmount: 1493.82,
      currency: 'EUR',
      itemsDescription: 'Papel A4',
    },
    ledgerAccountSuggestion: null,
    ledgerAccount: null,
    ...overrides,
  });

  const accounts: LedgerAccount[] = [
    { code: '628', name: 'Suministros' },
    { code: '629', name: 'Otros servicios' },
  ];

  const pending = (n: number, uploadedAt: string): Invoice =>
    invoice(n, { extractionStatus: 'Pending', extractedAt: null, extractedData: null, uploadedAt });

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

  /** Waits (up to 2 s) for the list to refresh by itself, and answers it. Timers are real, so no fixed wait. */
  async function respondToRefresh(body: PagedResult<Invoice>) {
    for (let attempt = 0; attempt < 200; attempt++) {
      TestBed.tick();
      const [request] = http.match((r) => r.url === invoicesUrl);
      if (request) {
        request.flush(body);
        await fixture.whenStable();
        return;
      }
      await sleep(10);
    }
    throw new Error('The list did not refresh.');
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(element().querySelectorAll<HTMLElement>('tr.mat-mdc-row'));

  const cellsOf = (row: HTMLElement) =>
    Array.from(row.querySelectorAll('td')).map((td) => td.textContent?.replace(/\s+/g, ' ').trim());
  const statusOf = (row: HTMLElement) =>
    row.querySelector('.status')?.textContent?.replace(/\s+/g, ' ').trim();
  const retryButton = (row: HTMLElement) =>
    row.querySelector<HTMLButtonElement>('button[aria-label="Volver a leer la factura"]');

  it('loads the first page and shows file, company, what was read, status, date and size', async () => {
    const params = await respond(pageOf([invoice(1)]));

    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
    expect(cellsOf(rows()[0])).toEqual([
      expect.stringContaining('factura-1.pdf'),
      'Acme S.L.',
      expect.stringContaining('Papelería Sol S.L.'),
      '1493,82 €',
      expect.stringContaining('Sin sugerencia'),
      'check_circle Leída',
      '22/10/2026 09:05',
      '245 KB',
    ]);
    expect(rows()[0].querySelector('.supplier-tax-id')?.textContent).toBe('B12345674');
    expect(retryButton(rows()[0])).toBeNull();
  });

  it('leaves the read columns empty until the invoice has been read', async () => {
    await respond(pageOf([invoice(1, { extractionStatus: 'Processing', extractedData: null })]));

    const cells = cellsOf(rows()[0]);
    expect(cells[2]).toBe('');
    expect(cells[3]).toBe('');
    // No account to pick before it's read
    expect(cells[4]).toBe('');
    expect(statusOf(rows()[0])).toBe('schedule Leyendo…');
    // Still being read: the list will refresh, answered here so the test ends cleanly
    await respondToRefresh(pageOf([invoice(1)]));
  });

  it('offers to read a failed invoice again, and queues it', async () => {
    const failed = invoice(1, {
      extractionStatus: 'Failed',
      extractionError: 'El archivo no es una factura.',
      extractedData: null,
    });
    await respond(pageOf([failed]));
    expect(statusOf(rows()[0])).toBe('error Error');

    retryButton(rows()[0])!.click();
    const request = http.expectOne(retryExtractionUrl('id-1'));
    expect(request.request.method).toBe('POST');
    request.flush(pending(1, new Date().toISOString()));
    await new Promise((resolve) => setTimeout(resolve));

    expect(openSnackBar).toHaveBeenCalledWith(
      'La factura se volverá a leer.',
      'OK',
      expect.anything(),
    );
    // The list is refreshed to show it Pending, and keeps refreshing until it is read
    await respond(pageOf([pending(1, new Date().toISOString())]));
    expect(retryButton(rows()[0])).toBeNull();
    await respondToRefresh(pageOf([invoice(1)]));
  });

  it('explains a conflict when someone else already retried it', async () => {
    await respond(pageOf([invoice(1, { extractionStatus: 'Failed', extractedData: null })]));

    retryButton(rows()[0])!.click();
    http.expectOne(retryExtractionUrl('id-1')).flush(null, { status: 409, statusText: 'Conflict' });
    await new Promise((resolve) => setTimeout(resolve));

    expect(openSnackBar).toHaveBeenCalledWith(
      'La factura ya se está leyendo o ya está leída.',
      'OK',
    );
    await respond(pageOf([invoice(1)]));
  });

  it('offers a retry only for invoices stuck in Pending', async () => {
    const recent = pending(1, new Date().toISOString());
    const stuck = pending(2, new Date(Date.now() - 10 * 60 * 1000).toISOString());
    await respond(pageOf([recent, stuck]));

    expect(retryButton(rows()[0])).toBeNull();
    expect(retryButton(rows()[1])).not.toBeNull();
    // The recent one is still expected to be read: the list refreshes for it
    await respondToRefresh(pageOf([invoice(1), stuck]));
  });

  it('refreshes while an invoice is being read, and stops when none is', async () => {
    await respond(pageOf([invoice(1, { extractionStatus: 'Processing', extractedData: null })]));

    await respondToRefresh(pageOf([invoice(1)]));
    expect(statusOf(rows()[0])).toBe('check_circle Leída');

    // Several intervals with nothing in progress: no request at all
    await sleep(refreshInterval * 5);
    TestBed.tick();
    http.expectNone((r) => r.url === invoicesUrl);
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

  it('reloads the list once an account is confirmed', async () => {
    const suggested = invoice(1, {
      ledgerAccountSuggestion: {
        code: '628',
        name: 'Suministros',
        source: 'Ai',
        reason: 'Luz.',
        suggestedAt: '2026-10-22T09:05:04',
      },
    });
    await respond(pageOf([suggested]));

    rows()[0]
      .querySelector<HTMLButtonElement>('button[aria-label="Confirmar la cuenta sugerida"]')!
      .click();
    http
      .expectOne((r) => r.method === 'PUT' && r.url === confirmLedgerAccountUrl('id-1'))
      .flush({ ...suggested, ledgerAccount: { code: '628', name: 'Suministros' } });
    // The cell emits once the save resolves; the list then asks for the page again
    await sleep(0);

    await respond(pageOf([{ ...suggested, ledgerAccount: { code: '628', name: 'Suministros' } }]));
    expect(cellsOf(rows()[0])[4]).toContain('Confirmada');
  });
});
