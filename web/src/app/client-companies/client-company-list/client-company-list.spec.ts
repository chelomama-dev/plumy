import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { PagedResult } from '../../shared/paged-result';
import { ClientCompany, clientCompaniesUrl } from '../client-company';
import { ClientCompanyList, searchDebounceMs } from './client-company-list';

describe('ClientCompanyList', () => {
  let fixture: ComponentFixture<ClientCompanyList>;
  let loader: HarnessLoader;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ClientCompanyList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(ClientCompanyList);
    loader = TestbedHarnessEnvironment.loader(fixture);
    http = TestBed.inject(HttpTestingController);
    // Not whenStable(): an in-flight httpResource request is a pending task, so it would wait forever
    TestBed.tick();
  });

  afterEach(() => http.verify());

  const company = (n: number): ClientCompany => ({
    id: `id-${n}`,
    name: `Company ${n}`,
    taxId: `B0000000${n}`,
    taxIdType: 'Cif',
  });

  const pageOf = (items: ClientCompany[], page = 1, totalCount = items.length) =>
    ({
      items,
      page,
      pageSize: 20,
      totalCount,
      totalPages: Math.ceil(totalCount / 20),
    }) satisfies PagedResult<ClientCompany>;

  /** Answers the pending list request and returns its query string. */
  async function respond(body: PagedResult<ClientCompany>) {
    TestBed.tick();
    const request = http.expectOne((r) => r.url === clientCompaniesUrl);
    request.flush(body);
    await fixture.whenStable();
    return request.request.params;
  }

  const rows = () =>
    Array.from(fixture.nativeElement.querySelectorAll('tr.mat-mdc-row') as NodeListOf<HTMLElement>);

  /** Clicked through the DOM: the harness would wait for stability, i.e. for the request it triggers. */
  const nextPage = () =>
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!
      .click();

  const waitForDebounce = () =>
    new Promise((resolve) => setTimeout(resolve, searchDebounceMs + 50));

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('loads the first page and renders the companies', async () => {
    const params = await respond(pageOf([company(1), company(2)], 1, 45));

    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
    expect(params.has('search')).toBe(false);

    expect(rows().map((r) => r.textContent)).toEqual([
      expect.stringContaining('Company 1'),
      expect.stringContaining('Company 2'),
    ]);
    expect(rows()[0].textContent).toContain('CIF');

    const paginator = await loader.getHarness(MatPaginatorHarness);
    expect(await paginator.getRangeLabel()).toBe('1 – 20 de 45');
  });

  it('requests the next page from the paginator (1-based in the API)', async () => {
    await respond(pageOf([company(1)], 1, 45));

    nextPage();

    const params = await respond(pageOf([company(21)], 2, 45));
    expect(params.get('page')).toBe('2');
  });

  it('debounces the search, trims it and goes back to the first page', async () => {
    await respond(pageOf([company(1)], 1, 45));
    nextPage();
    await respond(pageOf([company(21)], 2, 45));

    const searchBox = await loader.getHarness(MatInputHarness);
    await searchBox.setValue('  acme ');

    // Nothing is sent while the user is still typing
    TestBed.tick();
    http.expectNone((r) => r.url === clientCompaniesUrl);

    await waitForDebounce();
    const params = await respond(pageOf([company(3)]));

    expect(params.get('search')).toBe('acme');
    expect(params.get('page')).toBe('1');
  });

  it('shows a specific message when the search has no results', async () => {
    await respond(pageOf([]));
    expect(text()).toContain('Todavía no hay empresas cliente.');

    const searchBox = await loader.getHarness(MatInputHarness);
    await searchBox.setValue('zzz');
    await waitForDebounce();
    await respond(pageOf([]));

    expect(text()).toContain('Ninguna empresa coincide con «zzz».');
  });

  it('shows an error with a retry button when the request fails', async () => {
    http
      .expectOne((r) => r.url === clientCompaniesUrl)
      .flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(text()).toContain('No se han podido cargar las empresas cliente.');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.error button')!
      .click();
    await respond(pageOf([company(1)]));

    expect(text()).not.toContain('No se han podido cargar');
    expect(rows()).toHaveLength(1);
  });
});
