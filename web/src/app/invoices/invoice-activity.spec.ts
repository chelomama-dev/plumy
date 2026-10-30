import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { Invoice, invoiceUrl, retryExtractionUrl } from './invoice';
import {
  INVOICE_ACTIVITY_INTERVAL,
  InvoiceActivity,
  invoicePhase,
  suggestionOutcome,
} from './invoice-activity';

describe('InvoiceActivity', () => {
  const interval = 10;
  let activity: InvoiceActivity;
  let http: HttpTestingController;
  let openSnackBar: ReturnType<typeof vi.fn>;
  let snackAction: Subject<void>;

  const invoice = (n: number, overrides: Partial<Invoice> = {}): Invoice => ({
    id: `id-${n}`,
    clientCompanyId: 'acme-id',
    clientCompanyName: 'Acme S.L.',
    fileName: `factura-${n}.pdf`,
    contentType: 'application/pdf',
    sizeBytes: 1000,
    uploadedAt: new Date().toISOString(),
    extractionQueuedAt: new Date().toISOString(),
    extractionStatus: 'Pending',
    extractionError: null,
    extractedAt: null,
    extractedData: null,
    ledgerAccountSuggestion: null,
    suggestionStatus: null,
    noSuggestionReason: null,
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
    ...overrides,
  });

  const read = (n: number, overrides: Partial<Invoice> = {}) =>
    invoice(n, {
      extractionStatus: 'Extracted',
      suggestionStatus: 'Suggested',
      ledgerAccountSuggestion: {
        code: '628',
        name: 'Suministros',
        source: 'Ai',
        reason: 'Luz.',
        suggestedAt: '2026-10-27T09:00:00Z',
      },
      ...overrides,
    });

  beforeEach(() => {
    snackAction = new Subject<void>();
    openSnackBar = vi.fn(() => ({ onAction: () => snackAction }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: { open: openSnackBar } },
        { provide: INVOICE_ACTIVITY_INTERVAL, useValue: interval },
      ],
    });
    activity = TestBed.inject(InvoiceActivity);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Waits (up to 2 s) for the service to ask for the invoice, and answers it. */
  async function answer(id: string, body: Invoice | null, status = 200) {
    for (let attempt = 0; attempt < 200; attempt++) {
      const [request] = http.match(invoiceUrl(id));
      if (request) {
        request.flush(body, { status, statusText: status === 200 ? 'OK' : 'Error' });
        await new Promise((resolve) => setTimeout(resolve));
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 5));
    }
    throw new Error(`Nobody asked for ${id}.`);
  }

  it('follows an uploaded invoice and says when it is read, with the account suggested', async () => {
    activity.watch([invoice(1)]);
    expect(activity.active().map((i) => i.id)).toEqual(['id-1']);

    await answer('id-1', invoice(1, { extractionStatus: 'Processing' }));
    expect(invoicePhase(activity.active()[0])).toBe('reading');
    expect(activity.finished()).toBe(0);

    await answer('id-1', read(1));

    expect(activity.active()).toEqual([]);
    expect(activity.finished()).toBe(1);
    await vi.waitFor(() =>
      expect(openSnackBar).toHaveBeenCalledWith(
        '«factura-1.pdf» leída. Cuenta sugerida: 628 Suministros.',
        'Revisar',
        expect.anything(),
      ),
    );
  });

  it('opens the invoice when asked from the message', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    activity.watch([invoice(1)]);
    await answer('id-1', read(1));
    await vi.waitFor(() => expect(openSnackBar).toHaveBeenCalled());

    snackAction.next();

    expect(navigate).toHaveBeenCalledWith(['/invoices', 'id-1']);
  });

  it('waits for the AI to choose the account before saying it is done', async () => {
    activity.watch([invoice(1)]);

    await answer('id-1', read(1, { suggestionStatus: 'Pending', ledgerAccountSuggestion: null }));
    expect(invoicePhase(activity.active()[0])).toBe('suggesting');
    expect(openSnackBar).not.toHaveBeenCalled();

    await answer(
      'id-1',
      read(1, { suggestionStatus: 'NoSuggestion', ledgerAccountSuggestion: null }),
    );
    await vi.waitFor(() =>
      expect(openSnackBar).toHaveBeenCalledWith(
        '«factura-1.pdf» leída. La IA no ha sugerido cuenta.',
        'Revisar',
        expect.anything(),
      ),
    );
  });

  it('says a read failed, in Spanish, and offers to retry it', async () => {
    activity.watch([invoice(1)]);
    await answer(
      'id-1',
      invoice(1, {
        extractionStatus: 'Failed',
        extractionError: 'No invoice was found in the file.',
      }),
    );

    await vi.waitFor(() =>
      expect(openSnackBar).toHaveBeenCalledWith(
        'No se ha podido leer «factura-1.pdf». No se ha encontrado ninguna factura en el archivo.',
        'Reintentar',
        { panelClass: 'snack-error' },
      ),
    );

    snackAction.next();
    http
      .expectOne(retryExtractionUrl('id-1'))
      .flush(invoice(1, { extractionQueuedAt: new Date().toISOString() }));
    await new Promise((resolve) => setTimeout(resolve));

    // Followed again until the new attempt is done
    expect(activity.active().map((i) => i.id)).toEqual(['id-1']);
    await answer('id-1', read(1));
  });

  it('sums up several invoices finished at once', async () => {
    activity.watch([invoice(1), invoice(2), invoice(3)]);

    await Promise.all([
      answer('id-1', read(1)),
      answer('id-2', read(2)),
      answer('id-3', invoice(3, { extractionStatus: 'Failed', extractionError: 'x' })),
    ]);

    await vi.waitFor(() =>
      expect(openSnackBar).toHaveBeenCalledWith(
        '2 de 3 facturas leídas; 1 con error.',
        'Ver',
        expect.anything(),
      ),
    );
  });

  it('ignores invoices already finished, and stops asking once none is left', async () => {
    activity.watch([read(1)]);
    expect(activity.active()).toEqual([]);

    await new Promise((resolve) => setTimeout(resolve, interval * 5));
    http.expectNone(invoiceUrl('id-1'));
  });

  it('forgets an invoice that is gone, and keeps asking after a network error', async () => {
    activity.watch([invoice(1), invoice(2)]);

    await Promise.all([answer('id-1', null, 404), answer('id-2', null, 503)]);
    expect(activity.active().map((i) => i.id)).toEqual(['id-2']);

    await answer('id-2', read(2));
    expect(activity.active()).toEqual([]);
  });

  it('describes what came of the account', () => {
    expect(suggestionOutcome(read(1, { ledgerAccount: { code: '629', name: 'Otros' } }))).toBe(
      'Cuenta 629 confirmada.',
    );
    expect(
      suggestionOutcome(read(1, { suggestionStatus: 'Failed', ledgerAccountSuggestion: null })),
    ).toBe('No se pudo sugerir cuenta.');
  });
});
