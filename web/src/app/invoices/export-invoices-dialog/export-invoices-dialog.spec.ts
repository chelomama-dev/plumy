import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatRadioGroupHarness } from '@angular/material/radio/testing';
import { ClientCompany, clientCompaniesUrl } from '../../client-companies/client-company';
import { companySearchDebounceMs } from '../../client-companies/client-company-picker/client-company-picker';
import { PagedResult } from '../../shared/paged-result';
import { exportInvoicesUrl } from '../invoice';
import { exportErrorMessage, ExportInvoicesDialog } from './export-invoices-dialog';

describe('ExportInvoicesDialog', () => {
  let fixture: ComponentFixture<ExportInvoicesDialog>;
  let http: HttpTestingController;
  let loader: HarnessLoader;
  let close: ReturnType<typeof vi.fn>;
  let click: ReturnType<typeof vi.spyOn>;

  const acme: ClientCompany = {
    id: 'acme-id',
    name: 'Acme S.L.',
    taxId: 'B12345674',
    taxIdType: 'Cif',
  };

  beforeEach(() => {
    close = vi.fn();
    click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    URL.createObjectURL = vi.fn(() => 'blob:export');
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({
      imports: [ExportInvoicesDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(ExportInvoicesDialog);
    loader = TestbedHarnessEnvironment.loader(fixture);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => {
    http.verify();
    click.mockRestore();
  });

  const element = () => fixture.nativeElement as HTMLElement;
  const exportButton = () =>
    Array.from(element().querySelectorAll<HTMLButtonElement>('mat-dialog-actions button'))[1];

  async function respondCompanies(items: ClientCompany[]) {
    TestBed.tick();
    const page: PagedResult<ClientCompany> = {
      items,
      page: 1,
      pageSize: 10,
      totalCount: items.length,
      totalPages: 1,
    };
    http.expectOne((r) => r.url === clientCompaniesUrl).flush(page);
    await fixture.whenStable();
  }

  async function setDate(label: string, value: string) {
    const input = element().querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function clickExport(): Promise<TestRequest> {
    exportButton().click();
    await fixture.whenStable();
    return http.expectOne((r) => r.url === exportInvoicesUrl);
  }

  async function answer(request: TestRequest, fileName: string) {
    request.flush(new Blob(['x']), {
      headers: { 'Content-Disposition': `attachment; filename*=UTF-8''${fileName}` },
    });
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
  }

  it('exports every company to Excel by default, saving the file with the name the API gives', async () => {
    await respondCompanies([acme]);

    const request = await clickExport();
    expect(request.request.params.keys()).toEqual(['format']);
    expect(request.request.params.get('format')).toBe('xlsx');
    await answer(request, 'facturas-todas.xlsx');

    const link = click.mock.contexts[0] as HTMLAnchorElement;
    expect(link.download).toBe('facturas-todas.xlsx');
    expect(close).toHaveBeenCalledWith(true);
  });

  it('sends the company, the period and the format chosen', async () => {
    await respondCompanies([acme]);
    const autocomplete = await loader.getHarness(MatAutocompleteHarness);
    await autocomplete.enterText('acm');
    await new Promise((resolve) => setTimeout(resolve, companySearchDebounceMs + 50));
    await respondCompanies([acme]);
    await autocomplete.selectOption({ text: /Acme S\.L\./ });
    await setDate('Fecha desde', '2026-10-01');
    await setDate('Fecha hasta', '2026-10-31');
    await (await loader.getHarness(MatRadioGroupHarness)).checkRadioButton({ label: 'CSV' });

    const request = await clickExport();

    const params = request.request.params;
    expect(params.get('clientCompanyId')).toBe('acme-id');
    expect(params.get('from')).toBe('2026-10-01');
    expect(params.get('to')).toBe('2026-10-31');
    expect(params.get('format')).toBe('csv');
    await answer(request, 'facturas.csv');
  });

  it('does not export a period that ends before it starts', async () => {
    await respondCompanies([]);

    await setDate('Fecha desde', '2026-10-31');
    await setDate('Fecha hasta', '2026-10-01');

    expect(element().textContent).toContain('La fecha de inicio es posterior a la de fin.');
    expect(exportButton().disabled).toBe(true);
  });

  it('stays open and says why when the export fails', async () => {
    await respondCompanies([]);

    const request = await clickExport();
    request.flush(new Blob(['{}']), { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    // The error comes back as a rejected promise: one more tick before it shows
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();

    expect(element().textContent).toContain('Hay demasiadas facturas en ese periodo');
    expect(close).not.toHaveBeenCalled();
    expect(exportButton().disabled).toBe(false);
  });

  it('explains each failure in Spanish', async () => {
    await respondCompanies([]);
    const failure = (status: number) => new HttpErrorResponse({ status });

    expect(exportErrorMessage(failure(404))).toBe('La empresa ya no existe.');
    expect(exportErrorMessage(failure(0))).toBe('No se ha podido conectar con el servidor.');
    expect(exportErrorMessage(failure(500))).toBe('No se ha podido exportar.');
  });
});
