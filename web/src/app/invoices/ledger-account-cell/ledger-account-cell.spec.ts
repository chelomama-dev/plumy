import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import {
  confirmLedgerAccountUrl,
  Invoice,
  invoiceUrl,
  LedgerAccount,
  LedgerAccountSuggestion,
} from '../invoice';
import { LedgerAccountCell } from './ledger-account-cell';

describe('LedgerAccountCell', () => {
  let fixture: ComponentFixture<LedgerAccountCell>;
  let http: HttpTestingController;
  let loader: HarnessLoader;
  let openSnackBar: ReturnType<typeof vi.fn>;
  let changed: Invoice[];

  const accounts: LedgerAccount[] = [
    { code: '622', name: 'Reparaciones y conservación' },
    { code: '628', name: 'Suministros' },
    { code: '629', name: 'Otros servicios' },
  ];

  const aiSuggestion: LedgerAccountSuggestion = {
    code: '628',
    name: 'Suministros',
    source: 'Ai',
    reason: 'Es una factura de electricidad.',
    suggestedAt: '2026-10-24T09:00:00',
  };

  const invoice = (overrides: Partial<Invoice> = {}): Invoice => ({
    id: 'id-1',
    clientCompanyId: 'acme-id',
    clientCompanyName: 'Acme S.L.',
    fileName: 'luz.pdf',
    contentType: 'application/pdf',
    sizeBytes: 1000,
    uploadedAt: '2026-10-24T09:00:00',
    extractionQueuedAt: '2026-10-24T09:00:00',
    suggestionStatus: 'Suggested',
    noSuggestionReason: null,
    extractionStatus: 'Extracted',
    extractionError: null,
    extractedAt: '2026-10-24T09:00:03',
    extractedData: null,
    ledgerAccountSuggestion: aiSuggestion,
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

  async function render(value: Invoice, catalog: LedgerAccount[] = accounts) {
    fixture.componentRef.setInput('invoice', value);
    fixture.componentRef.setInput('accounts', catalog);
    await fixture.whenStable();
  }

  beforeEach(() => {
    openSnackBar = vi.fn();
    changed = [];
    TestBed.configureTestingModule({
      imports: [LedgerAccountCell],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open: openSnackBar } },
      ],
    });
    fixture = TestBed.createComponent(LedgerAccountCell);
    fixture.componentInstance.changed.subscribe((value) => changed.push(value));
    http = TestBed.inject(HttpTestingController);
    loader = TestbedHarnessEnvironment.loader(fixture);
  });

  afterEach(() => http.verify());

  const text = () =>
    (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim();
  const source = () =>
    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.source')!;
  /** The tooltip's text, read from the component: the tooltip itself only renders on hover. */
  const explanation = () =>
    (fixture.componentInstance as unknown as { explanation: () => string }).explanation();
  const confirmButton = () =>
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[aria-label="Confirmar la cuenta sugerida"]',
    );

  it('shows nothing until the invoice has been read', async () => {
    await render(invoice({ extractionStatus: 'Processing', ledgerAccountSuggestion: null }));

    expect(text()).toBe('');
  });

  it('shows the AI suggestion, ready to confirm', async () => {
    await render(invoice());
    const select = await loader.getHarness(MatSelectHarness);

    expect(await select.getValueText()).toBe('628 · Suministros');
    expect(text()).toContain('Sugerida por IA');
    expect(confirmButton()).not.toBeNull();
  });

  it('locks the account of an approved or rejected invoice', async () => {
    await render(invoice({ reviewStatus: 'Approved' }));
    const select = await loader.getHarness(MatSelectHarness);

    expect(await select.isDisabled()).toBe(true);
    expect(confirmButton()).toBeNull();
  });

  it('says when the suggestion comes from the supplier history', async () => {
    await render(
      invoice({
        ledgerAccountSuggestion: { ...aiSuggestion, source: 'SupplierHistory', reason: null },
      }),
    );

    expect(text()).toContain('Como las anteriores del proveedor');
  });

  it('confirms the suggestion with one click', async () => {
    await render(invoice());

    confirmButton()!.click();
    const request = http.expectOne(confirmLedgerAccountUrl('id-1'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ code: '628' });
    expect(request.request.headers.get('If-Match')).toBe('"00000000000007D1"');
    const confirmed = invoice({ ledgerAccount: { code: '628', name: 'Suministros' } });
    request.flush(confirmed);
    await fixture.whenStable();

    expect(changed).toEqual([confirmed]);
  });

  it('saves another account picked from the list', async () => {
    await render(invoice());
    const select = await loader.getHarness(MatSelectHarness);

    await select.open();
    await select.clickOptions({ text: '622 · Reparaciones y conservación' });

    const request = http.expectOne(confirmLedgerAccountUrl('id-1'));
    expect(request.request.body).toEqual({ code: '622' });
    request.flush(invoice({ ledgerAccount: { code: '622', name: 'Reparaciones y conservación' } }));
    await fixture.whenStable();
    expect(changed.length).toBe(1);
  });

  it('puts the account back when saving fails', async () => {
    await render(invoice());
    const select = await loader.getHarness(MatSelectHarness);

    await select.open();
    await select.clickOptions({ text: '629 · Otros servicios' });
    http
      .expectOne(confirmLedgerAccountUrl('id-1'))
      .flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(await select.getValueText()).toBe('628 · Suministros');
    expect(openSnackBar).toHaveBeenCalledWith('No se ha podido guardar la cuenta.', 'OK');
    expect(changed).toEqual([]);
  });

  it('shows the invoice as it is now when someone else changed it meanwhile', async () => {
    await render(invoice());

    confirmButton()!.click();
    http
      .expectOne(confirmLedgerAccountUrl('id-1'))
      .flush(null, { status: 412, statusText: 'Precondition Failed' });
    await fixture.whenStable();
    const current = invoice({
      ledgerAccount: { code: '629', name: 'Otros servicios' },
      version: '00000000000007D2',
    });
    http.expectOne(invoiceUrl('id-1')).flush(current);
    await fixture.whenStable();

    expect(openSnackBar).toHaveBeenCalledWith(
      'Alguien ha cambiado la factura mientras la tenías abierta. Ahora ves cómo está.',
      'OK',
    );
    expect(changed).toEqual([current]);
  });

  it('shows a confirmed account as such, with nothing left to confirm', async () => {
    await render(invoice({ ledgerAccount: { code: '622', name: 'Reparaciones y conservación' } }));
    const select = await loader.getHarness(MatSelectHarness);

    expect(await select.getValueText()).toBe('622 · Reparaciones y conservación');
    expect(text()).toContain('Confirmada');
    expect(confirmButton()).toBeNull();
  });

  it('says when the account was set by Plumy approving the invoice by itself', async () => {
    await render(
      invoice({
        ledgerAccount: { code: '629', name: 'Otros servicios' },
        reviewStatus: 'Approved',
        approvedAutomatically: true,
      }),
    );

    expect(text()).toContain('Puesta por Plumy');
    expect(text()).not.toContain('Confirmada');
  });

  it('shows the account while the catalog is still loading', async () => {
    await render(invoice(), []);
    const select = await loader.getHarness(MatSelectHarness);

    expect(await select.getValueText()).toBe('628 · Suministros');
  });

  it('offers to pick one when there is no suggestion', async () => {
    await render(invoice({ ledgerAccountSuggestion: null }));
    const select = await loader.getHarness(MatSelectHarness);

    expect(await select.getValueText()).toBe('Elegir cuenta');
    expect(text()).toContain('Sin sugerencia');
    expect(confirmButton()).toBeNull();
  });

  it('says the AI is still choosing while the suggestion is pending', async () => {
    await render(invoice({ ledgerAccountSuggestion: null, suggestionStatus: 'Pending' }));

    expect(text()).toContain('La IA está eligiendo cuenta…');
  });

  it('says the AI made no suggestion, and explains why with its own words', async () => {
    await render(
      invoice({
        ledgerAccountSuggestion: null,
        suggestionStatus: 'NoSuggestion',
        noSuggestionReason: 'Los conceptos no dicen qué se compró.',
      }),
    );

    expect(text()).toContain('La IA no ha sugerido cuenta');
    expect(source().querySelector('mat-icon')?.textContent).toBe('help_outline');
    expect(explanation()).toBe('Los conceptos no dicen qué se compró.');
  });

  it('explains a missing reason in general terms', async () => {
    await render(invoice({ ledgerAccountSuggestion: null, suggestionStatus: 'NoSuggestion' }));

    expect(explanation()).toContain('No hay datos suficientes');
  });

  it('says when the suggestion failed', async () => {
    await render(invoice({ ledgerAccountSuggestion: null, suggestionStatus: 'Failed' }));

    expect(text()).toContain('No se pudo sugerir cuenta');
    expect(explanation()).toContain('Elige la cuenta a mano');
  });

  it('is no longer choosing once a person picked the account', async () => {
    await render(
      invoice({
        ledgerAccountSuggestion: null,
        suggestionStatus: 'Pending',
        ledgerAccount: { code: '629', name: 'Otros servicios' },
      }),
    );

    expect(text()).toContain('Confirmada');
    expect(text()).not.toContain('eligiendo');
  });
});
