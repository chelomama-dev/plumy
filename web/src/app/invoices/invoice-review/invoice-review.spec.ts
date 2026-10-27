import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import {
  confirmLedgerAccountUrl,
  Invoice,
  invoiceDataUrl,
  invoicesUrl,
  invoiceUrl,
  LedgerAccount,
  ledgerAccountsUrl,
  reviewInvoiceUrl,
} from '../invoice';
import { RejectInvoiceDialog } from '../reject-invoice-dialog/reject-invoice-dialog';
import { InvoiceReview } from './invoice-review';

describe('InvoiceReview', () => {
  let fixture: ComponentFixture<InvoiceReview>;
  let http: HttpTestingController;
  let openSnackBar: ReturnType<typeof vi.fn>;
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
    reviewedAt: null,
    rejectionReason: null,
    ...overrides,
  });

  const withAccount = (overrides: Partial<Invoice> = {}) =>
    invoice({ ledgerAccount: { code: '628', name: 'Suministros' }, ...overrides });

  beforeEach(() => {
    openSnackBar = vi.fn();
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
    expect(field('Total').hint()).toBe('help Revísalo: la lectura no es segura (41 %)');
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
    request.flush(withAccount({ reviewStatus: 'Approved', reviewedAt: '2026-10-26T10:00:00' }));
    await fixture.whenStable();

    expect(text()).toContain('Aprobada el 26/10/2026 10:00');
    expect(field('Nombre').input.disabled).toBe(true);
    expect(button('Aprobar')).toBeUndefined();
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

    expect(text()).toContain('No se ha podido leer: No es legible.');
    expect(element().querySelector('form')).toBeNull();
    expect(button('Rechazar')).toBeDefined();
    expect(button('Aprobar')).toBeUndefined();
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
      'Alguien ha cambiado la factura mientras tanto. Revisa cómo está ahora.',
      'OK',
    );
    TestBed.tick();
    http.expectOne(invoiceUrl('id-1')).flush(withAccount({ reviewStatus: 'Approved' }));
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
