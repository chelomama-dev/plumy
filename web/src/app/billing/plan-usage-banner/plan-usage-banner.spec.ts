import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subscription, subscriptionUrl } from '../subscription';
import { PlanUsageBanner } from './plan-usage-banner';

const subscription = (overrides: Partial<Subscription> = {}): Subscription => ({
  plan: 'Trial',
  status: 'Trialing',
  periodStart: '2026-11-01T10:00:00Z',
  periodEnd: '2026-11-15T10:00:00Z',
  invoiceLimit: 50,
  uploadedInPeriod: 0,
  uploadBlock: 'None',
  ...overrides,
});

describe('PlanUsageBanner', () => {
  let fixture: ComponentFixture<PlanUsageBanner>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlanUsageBanner],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(answer: Subscription | 'error') {
    fixture = TestBed.createComponent(PlanUsageBanner);
    TestBed.tick();
    const request = http.expectOne(subscriptionUrl);
    if (answer === 'error') {
      request.flush(null, { status: 500, statusText: 'Error' });
    } else {
      request.flush(answer);
    }
    await fixture.whenStable();
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('stays out of the way while there is room', async () => {
    await render(subscription({ uploadedInPeriod: 10 }));

    expect(text()).toBe('');
  });

  it('warns when the plan is running out, with the way to the plan page', async () => {
    await render(subscription({ uploadedInPeriod: 45 }));

    expect(text()).toContain('Quedan 5 de las 50 facturas del plan en este periodo.');
    expect(element().querySelector('a')?.getAttribute('href')).toBe('/billing');
  });

  it('says why uploads stopped', async () => {
    await render(subscription({ uploadBlock: 'TrialEnded' }));

    expect(element().querySelector('[role="alert"]')?.textContent).toContain(
      'La prueba gratuita ha terminado.',
    );
  });

  it('shows nothing if the plan cannot be read', async () => {
    await render('error');

    expect(text()).toBe('');
  });
});
