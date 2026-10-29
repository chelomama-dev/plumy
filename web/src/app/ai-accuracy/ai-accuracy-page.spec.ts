import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { clientCompaniesUrl } from '../client-companies/client-company';
import { AiAccuracyReport, aiAccuracyUrl, isDoubtfulBand, periodStart, rate } from './ai-accuracy';
import { AiAccuracyPage, formatRate, TODAY } from './ai-accuracy-page';

describe('ai accuracy helpers', () => {
  it('starts a period so that it includes today', () => {
    expect(periodStart(30, new Date('2026-10-29T23:30:00Z'))).toBe('2026-09-30');
    expect(periodStart(1, new Date('2026-10-29T08:00:00Z'))).toBe('2026-10-29');
  });

  it('has no rate out of nothing', () => {
    expect(rate(3, 4)).toBe(0.75);
    expect(rate(0, 0)).toBeNull();
    expect(formatRate(null)).toBe('—');
    expect(formatRate(0.856)).toBe('86 %');
  });

  it('calls doubtful the bands below the review screen threshold', () => {
    expect(isDoubtfulBand({ from: 0.5, to: 0.8, read: 0, corrected: 0 })).toBe(true);
    expect(isDoubtfulBand({ from: 0.8, to: 0.9, read: 0, corrected: 0 })).toBe(false);
  });
});

describe('AiAccuracyPage', () => {
  let fixture: ComponentFixture<AiAccuracyPage>;
  let http: HttpTestingController;

  const report = (overrides: Partial<AiAccuracyReport> = {}): AiAccuracyReport => ({
    approvedInvoices: 20,
    ledgerAccounts: {
      ai: { suggested: 8, accepted: 6 },
      supplierHistory: { suggested: 10, accepted: 10 },
      notSuggested: 2,
    },
    fields: [
      { field: 'supplierName', read: 20, corrected: 1, missed: 0 },
      { field: 'netAmount', read: 18, corrected: 6, missed: 2 },
      { field: 'currency', read: 0, corrected: 0, missed: 0 },
    ],
    confidenceBands: [
      { from: 0, to: 0.5, read: 2, corrected: 2 },
      { from: 0.5, to: 0.8, read: 4, corrected: 3 },
      { from: 0.8, to: 0.9, read: 0, corrected: 0 },
      { from: 0.9, to: 0.95, read: 10, corrected: 1 },
      { from: 0.95, to: 1, read: 22, corrected: 1 },
    ],
    ...overrides,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AiAccuracyPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TODAY, useValue: () => new Date('2026-10-29T10:00:00Z') },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function settle() {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  /** Opens the page and answers the company search; returns the report request. */
  function render() {
    fixture = TestBed.createComponent(AiAccuracyPage);
    TestBed.tick();
    http
      .match((r) => r.url === clientCompaniesUrl)
      .forEach((r) => r.flush({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }));
    return http.expectOne((r) => r.url === aiAccuracyUrl);
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const rows = (title: string) =>
    [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll(
        `section[aria-labelledby=${title}] tbody tr`,
      ),
    ].map((row) =>
      [...row.children].map((cell) => cell.textContent?.replace(/\s+/g, ' ').trim()).join(' | '),
    );

  it('asks for the last 90 days of every company by default', async () => {
    const request = render();

    expect(request.request.params.get('from')).toBe('2026-08-01');
    expect(request.request.params.has('clientCompanyId')).toBe(false);
    request.flush(report());
    await settle();
  });

  it('shows how often the suggestions and the fields were kept', async () => {
    render().flush(report());
    await settle();

    expect(text()).toContain('75 %');
    expect(text()).toContain('6 de 8 sugerencias');
    expect(text()).toContain('100 %');
    expect(text()).toContain('En 2 facturas no hubo sugerencia');
  });

  it('lists the fields worst first, leaving out those never read', async () => {
    render().flush(report());
    await settle();

    const fields = rows('fields-title');
    expect(fields).toHaveLength(2);
    expect(fields[0]).toBe('Base imponible | 18 | 6 | 67 % | 2');
    expect(fields[1]).toMatch(/^Nombre del proveedor/);
  });

  it('lists the confidence bands from high to low, marking the doubtful ones', async () => {
    render().flush(report());
    await settle();

    const bands = rows('bands-title');
    expect(bands[0]).toBe('95–100 % | 22 | 1 | 95 %');
    expect(bands[2]).toContain('—');
    expect(bands[3]).toMatch(/^50–80 % Dudosa \|/);
  });

  it('explains an empty period instead of showing zeros', async () => {
    render().flush(report({ approvedInvoices: 0 }));
    await settle();

    expect(text()).toContain('Aún no hay facturas aprobadas en este periodo.');
    expect(rows('fields-title')).toHaveLength(0);
  });

  it('asks for everything when the period is "Todo"', async () => {
    render().flush(report());
    await settle();

    const todo = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('mat-button-toggle button'),
    ].find((b) => b.textContent?.includes('Todo')) as HTMLButtonElement;
    todo.click();
    TestBed.tick();

    const request = http.expectOne((r) => r.url === aiAccuracyUrl);
    expect(request.request.params.has('from')).toBe(false);
    request.flush(report());
    await settle();
  });
});
