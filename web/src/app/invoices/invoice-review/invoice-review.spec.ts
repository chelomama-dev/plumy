import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter, Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import {
  confirmLedgerAccountUrl,
  Invoice,
  invoiceDataUrl,
  invoicesUrl,
  invoiceUrl,
  LedgerAccount,
  ledgerAccountsUrl,
  retryExtractionUrl,
  reviewInvoiceUrl,
} from '../invoice';
import { INVOICE_ACTIVITY_INTERVAL } from '../invoice-activity';
import { INVOICE_REFRESH_INTERVAL } from '../invoice-list/invoice-list';
import { RejectInvoiceDialog } from '../reject-invoice-dialog/reject-invoice-dialog';
import { InvoiceReview } from './invoice-review';

describe('InvoiceReview', () => {
  let fixture: ComponentFixture<InvoiceReview>;
  let http: HttpTestingController;
  let openSnackBar: ReturnType<typeof vi.fn>;
  let snackBarAction: Subject<void>;
  let openDialog: ReturnType<typeof vi.fn>;
  let dialogResult: string | undefined;

  const accounts: LedgerAccount[] = [{ code: '628', name: 'Suministros' }];

  const invoice = (overrides: Partial<Invoice> = {}): Invoice => ({
    id: 'id-1',
    clientCompanyId: 'acme-id',
    clientCompanyName: 'Acme S.L.',
    fileName: 'luz.png',
    contentType: 'image/png',
    sizeBytes: 1000,
    uploadedAt: '2026-10-26T09:00:00',
    extractionQueuedAt: '2026-10-26T09:00:00',
    suggestionStatus: 'NoSuggestion',
    noSuggestionReason: null,
    extractionStatus: 'Extracted',
    extractionError: null,
    extractedAt: '2026-10-26T09:00:03',
    extractedData: {
      supplierName: 'Endesa',
      supplierTaxId: 'A81948077',
      customerTaxId: null,
      invoiceNumber: 'F-42',
      issueDate: '2026-10-01',
      netAmount: 100,
      taxAmount: 21,
      totalAmount: 12.1,
      currency: 'EUR',
      itemsDescription: 'Electricidad',
    },
    issues: ['AmountsMismatch'],
    duplicateOfInvoiceId: null,
    ledgerAccountSuggestion: null,
    ledgerAccount: null,
    fieldConfidence: { supplierName: 0.98, totalAmount: 0.41 },
    correctedFields: [],
    reviewStatus: 'Unreviewed',
    approvedAutomatically: false,
    reviewedAt: null,
    rejectionReason: null,
    version: '00000000000007D1',
    ...overrides,
  });

  const withAccount = (overrides: Partial<Invoice> = {}) =>
    invoice({ ledgerAccount: { code: '628', name: 'Suministros' }, ...overrides });

  beforeEach(() => {
    snackBarAction = new Subject<void>();
    openSnackBar = vi.fn(() => ({ onAction: () => snackBarAction }));
    dialogResult = undefined;
    openDialog = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    // jsdom has no object URLs
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();

    TestBed.configureTestingModule({
      imports: [InvoiceReview],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: { open: openSnackBar } },
        { provide: MatDialog, useValue: { open: openDialog } },
        { provide: INVOICE_REFRESH_INTERVAL, useValue: 20 },
        { provide: INVOICE_ACTIVITY_INTERVAL, useValue: 60 * 60 * 1000 },
      ],
    });
    fixture = TestBed.createComponent(InvoiceReview);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Opens the page and answers what it loads: the invoice, the chart of accounts and the file. */
  async function open(value: Invoice) {
    fixture.componentRef.setInput('id', value.id);
    TestBed.tick();
    http.expectOne(invoiceUrl(value.id)).flush(value);
    http.expectOne(ledgerAccountsUrl).flush(accounts);
    http
      .expectOne(`${invoicesUrl}/${value.id}/file`)
      .flush(new Blob(['png'], { type: value.contentType }));
    await fixture.whenStable();
  }

  /** Lets the component's awaits (dialog, then request) run, then renders. */
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent?.replace(/\s+/g, ' ') ?? '';
  const field = (label: string) => {
    const formField = Array.from(element().querySelectorAll('mat-form-field')).find(
      (f) => f.querySelector('mat-label')?.textContent?.trim() === label,
    )!;
    return {
      input: formField.querySelector<HTMLInputElement | HTMLTextAreaElement>('input, textarea')!,
      hint: () => formField.querySelector('mat-hint')?.textContent?.replace(/\s+/g, ' ').trim(),
    };
  };
  const type = async (label: string, value: string) => {
    const { input } = field(label);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const button = (label: string) =>
    Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes(label),
    );

  it('shows the document next to what was read, and what to look at', async () => {
    await open(invoice());

    expect(element().querySelector('img')?.getAttribute('src')).toBe('blob:preview');
    expect(field('Nombre').input.value).toBe('Endesa');
    expect(field('Total').input.value).toBe('12,10');
    expect(field('Fecha').input.value).toBe('2026-10-01');
    expect(text()).toContain('Base + IVA no da el total');
    // Only where the reader was unsure
    expect(field('Total').hint()).toBe('help Dudosa (41 %)');
    expect(field('Nombre').hint()).toBeUndefined();
  });

  it('shows a PDF in a frame', async () => {
    await open(invoice({ contentType: 'application/pdf', fileName: 'luz.pdf' }));

    expect(element().querySelector('iframe')?.getAttribute('src')).toBe('blob:preview');
  });

  it('saves the corrections, as numbers, and shows what the API made of them', async () => {
    await open(withAccount());

    await type('Total', '121,00');
    expect(button('Aprobar')!.disabled).toBe(true);
    button('Guardar cambios')!.click();

    const request = http.expectOne(invoiceDataUrl('id-1'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"00000000000007D1"');
    expect(request.request.body).toEqual({
      supplierName: 'Endesa',
      supplierTaxId: 'A81948077',
      customerTaxId: null,
      invoiceNumber: 'F-42',
      issueDate: '2026-10-01',
      netAmount: 100,
      taxAmount: 21,
      totalAmount: 121,
      currency: 'EUR',
      itemsDescription: 'Electricidad',
    });
    request.flush(
      withAccount({
        extractedData: { ...invoice().extractedData!, totalAmount: 121 },
        issues: [],
        correctedFields: ['totalAmount'],
      }),
    );
    await fixture.whenStable();

    expect(field('Total').hint()).toBe('edit Corregido a mano');
    expect(text()).not.toContain('Base + IVA no da el total');
    expect(button('Aprobar')!.disabled).toBe(false);
  });

  it('does not save an amount that is not one', async () => {
    await open(invoice());

    await type('Total', 'ciento veintiuno');
    button('Guardar cambios')!.click();
    await fixture.whenStable();

    expect(text()).toContain('Escribe un importe, como 1234,56.');
    http.expectNone(invoiceDataUrl('id-1'));
  });

  it('needs the account confirmed before approving', async () => {
    await open(invoice());

    expect(button('Aprobar')!.disabled).toBe(true);
  });

  it('keeps what the person typed when the account is confirmed', async () => {
    await open(
      invoice({
        ledgerAccountSuggestion: {
          code: '628',
          name: 'Suministros',
          source: 'Ai',
          reason: null,
          suggestedAt: '2026-10-26T09:00:04',
        },
      }),
    );
    await type('Número', 'F-43');

    element()
      .querySelector<HTMLButtonElement>('button[aria-label="Confirmar la cuenta sugerida"]')!
      .click();
    http.expectOne(confirmLedgerAccountUrl('id-1')).flush(withAccount());
    await fixture.whenStable();

    expect(field('Número').input.value).toBe('F-43');
  });

  it('approves, then shows it approved and locked', async () => {
    await open(withAccount());

    button('Aprobar')!.click();
    const request = http.expectOne(reviewInvoiceUrl('id-1'));
    expect(request.request.body).toEqual({ status: 'Approved', rejectionReason: null });
    expect(request.request.headers.get('If-Match')).toBe('"00000000000007D1"');
    request.flush(withAccount({ reviewStatus: 'Approved', reviewedAt: '2026-10-26T10:00:00' }));
    await fixture.whenStable();

    expect(text()).toContain('Aprobada el 26/10/2026 10:00');
    expect(field('Nombre').input.disabled).toBe(true);
    expect(button('Aprobar')).toBeUndefined();
    expect(button('Reabrir')).toBeDefined();
  });

  it('says when Plumy approved it by itself, and lets a person reopen it', async () => {
    await open(
      withAccount({
        reviewStatus: 'Approved',
        approvedAutomatically: true,
        reviewedAt: '2026-10-26T10:00:00',
      }),
    );

    expect(text()).toContain('Aprobada automáticamente el 26/10/2026 10:00');
    expect(text()).toContain('proveedor con cuenta ya conocida');
    expect(button('Reabrir')).toBeDefined();
  });

  it('rejects with the reason given in the dialog', async () => {
    await open(invoice());
    dialogResult = 'Duplicada';

    button('Rechazar')!.click();
    await fixture.whenStable();

    expect(openDialog).toHaveBeenCalledWith(RejectInvoiceDialog, expect.anything());
    const request = http.expectOne(reviewInvoiceUrl('id-1'));
    expect(request.request.body).toEqual({ status: 'Rejected', rejectionReason: 'Duplicada' });
    request.flush(invoice({ reviewStatus: 'Rejected', rejectionReason: 'Duplicada' }));
    await settle();

    expect(text()).toContain('Rechazada: Duplicada');
  });

  it('does nothing when the rejection is cancelled', async () => {
    await open(invoice());

    button('Rechazar')!.click();
    await fixture.whenStable();

    http.expectNone(reviewInvoiceUrl('id-1'));
  });

  it('can reject an invoice that could not be read', async () => {
    await open(
      invoice({
        extractionStatus: 'Failed',
        extractionError: 'No es legible.',
        extractedData: null,
        issues: [],
      }),
    );

    expect(text()).toContain('No es legible.');
    expect(element().querySelector('form')).toBeNull();
    expect(button('Rechazar')).toBeDefined();
    expect(button('Aprobar')).toBeUndefined();
  });

  it('reads a failed invoice again from here, and waits for it without a reload button', async () => {
    const failed = invoice({
      extractionStatus: 'Failed',
      // As the worker writes it: shown in Spanish
      extractionError: 'No invoice was found in the file.',
      extractedData: null,
      issues: [],
    });
    await open(failed);
    expect(text()).toContain('No se ha encontrado ninguna factura en el archivo.');

    button('Volver a leer')!.click();
    http.expectOne(retryExtractionUrl('id-1')).flush({
      ...failed,
      extractionStatus: 'Pending',
      extractionError: null,
      extractionQueuedAt: new Date().toISOString(),
    });
    await settle();

    expect(text()).toContain('En cola: la IA la leerá en unos segundos.');
    expect(button('Actualizar')).toBeUndefined();

    // It looks again by itself until the AI is done
    await new Promise((resolve) => setTimeout(resolve, 60));
    http.expectOne(invoiceUrl('id-1')).flush(invoice());
    await fixture.whenStable();
    expect(field('Nombre').input.value).toBe('Endesa');
  });

  it('reopens an approved invoice', async () => {
    await open(withAccount({ reviewStatus: 'Approved', reviewedAt: '2026-10-26T10:00:00' }));

    button('Reabrir')!.click();
    const request = http.expectOne(reviewInvoiceUrl('id-1'));
    expect(request.request.body).toEqual({ status: 'Unreviewed', rejectionReason: null });
    request.flush(withAccount());
    await fixture.whenStable();

    expect(field('Nombre').input.disabled).toBe(false);
  });

  it('says so and shows the invoice as it is now when someone else changed it', async () => {
    await open(withAccount());

    button('Aprobar')!.click();
    http.expectOne(reviewInvoiceUrl('id-1')).flush(null, { status: 409, statusText: 'Conflict' });
    // Not whenStable: it would wait for the reload, which is answered below
    await new Promise((resolve) => setTimeout(resolve));

    expect(openSnackBar).toHaveBeenCalledWith(
      'Alguien ha cambiado la factura mientras la tenías abierta. Revisa cómo está ahora.',
      'OK',
    );
    TestBed.tick();
    http.expectOne(invoiceUrl('id-1')).flush(withAccount({ reviewStatus: 'Approved' }));
    await fixture.whenStable();
    expect(button('Reabrir')).toBeDefined();
  });

  it('keeps the form when its copy is out of date, and loads the changes when asked', async () => {
    await open(withAccount());

    await type('Total', '121,00');
    button('Guardar cambios')!.click();
    http
      .expectOne(invoiceDataUrl('id-1'))
      .flush(null, { status: 412, statusText: 'Precondition Failed' });
    await fixture.whenStable();

    // What the person typed is still there, and nothing was loaded over it
    expect(field('Total').input.value).toBe('121,00');
    expect(text()).toContain('Pulsa «Ver cambios» para cargarla');
    expect(openSnackBar).toHaveBeenCalledWith(
      'Alguien ha cambiado la factura mientras la tenías abierta.',
      'Ver cambios',
    );
    http.expectNone(invoiceUrl('id-1'));

    snackBarAction.next();
    TestBed.tick();
    http
      .expectOne(invoiceUrl('id-1'))
      .flush(withAccount({ reviewStatus: 'Approved', version: '00000000000007D2' }));
    await fixture.whenStable();
    expect(button('Reabrir')).toBeDefined();
  });

  it('goes on to the next invoice to review', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await open(withAccount({ reviewStatus: 'Approved' }));

    button('Siguiente por revisar')!.click();
    const request = http.expectOne((r) => r.url === invoicesUrl);
    expect(request.request.params.get('reviewStatus')).toBe('Unreviewed');
    request.flush({
      items: [invoice({ id: 'id-1' }), invoice({ id: 'id-2' })],
      page: 1,
      pageSize: 2,
      totalCount: 2,
      totalPages: 1,
    });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/invoices', 'id-2']);
  });
});
