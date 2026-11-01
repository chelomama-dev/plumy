import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CurrentUserService } from '../core/firm/current-user.service';
import { BILLING_REDIRECT, BillingPage, CHECKOUT_POLL } from './billing-page';
import { checkoutUrl, portalUrl, Subscription, subscriptionUrl } from './subscription';

const trial = (overrides: Partial<Subscription> = {}): Subscription => ({
  plan: 'Trial',
  status: 'Trialing',
  periodStart: '2026-11-01T10:00:00Z',
  periodEnd: '2026-11-15T10:00:00Z',
  invoiceLimit: 50,
  uploadedInPeriod: 12,
  uploadBlock: 'None',
  ...overrides,
});

const basic = (overrides: Partial<Subscription> = {}): Subscription =>
  trial({
    plan: 'Basic',
    status: 'Active',
    periodEnd: '2026-12-01T10:00:00Z',
    invoiceLimit: 300,
    ...overrides,
  });

describe('BillingPage', () => {
  let fixture: ComponentFixture<BillingPage>;
  let http: HttpTestingController;
  const role = signal<'Owner' | 'Member'>('Owner');
  const redirect = vi.fn();

  beforeEach(() => {
    role.set('Owner');
    redirect.mockClear();
    TestBed.configureTestingModule({
      imports: [BillingPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: CurrentUserService,
          useValue: { firm: () => ({ id: 'f', name: 'G', role: role() }) },
        },
        { provide: BILLING_REDIRECT, useValue: redirect },
        { provide: CHECKOUT_POLL, useValue: { intervalMs: 1, attempts: 2 } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(subscription: Subscription, checkout?: string) {
    fixture = TestBed.createComponent(BillingPage);
    if (checkout) {
      fixture.componentRef.setInput('checkout', checkout);
    }
    TestBed.tick();
    await respond(subscription);
  }

  async function respond(subscription: Subscription) {
    http.expectOne(subscriptionUrl).flush(subscription);
    await fixture.whenStable();
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent?.replace(/\s+/g, ' ') ?? '';
  const button = (label: string) =>
    [...element().querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes(label),
    );
  const waitForTimers = () => new Promise((resolve) => setTimeout(resolve, 5));

  it('shows the trial, how much of it is used and the plans to choose from', async () => {
    await render(trial());

    expect(text()).toContain('Prueba gratuita');
    expect(text()).toContain('En prueba');
    expect(text()).toContain('Prueba hasta el 15/11/2026, sin tarjeta.');
    expect(text()).toContain('12 de 50 facturas en la prueba');
    expect(button('Elegir Basic')).toBeTruthy();
    expect(button('Elegir Pro')).toBeTruthy();
    expect(button('Gestionar suscripción')).toBeUndefined();
  });

  it('sends the owner to Stripe with the plan they chose', async () => {
    await render(trial());

    button('Elegir Pro')!.click();
    const request = http.expectOne(checkoutUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ plan: 'Pro' });
    request.flush({ url: 'https://checkout.stripe.com/c/pay/cs_test_1' });
    await fixture.whenStable();

    expect(redirect).toHaveBeenCalledWith('https://checkout.stripe.com/c/pay/cs_test_1');
  });

  it('says so when payments are unavailable, and reads the plan again', async () => {
    await render(trial());

    button('Elegir Basic')!.click();
    http.expectOne(checkoutUrl).flush(null, { status: 503, statusText: 'Unavailable' });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
    await respond(trial());

    expect(redirect).not.toHaveBeenCalled();
    expect(text()).toContain('Los pagos no están disponibles ahora mismo.');
    expect(button('Elegir Basic')!.disabled).toBe(false);
  });

  it('offers the portal, not the plans, once the firm pays', async () => {
    await render(basic({ uploadedInPeriod: 40 }));

    expect(text()).toContain('Al día');
    expect(text()).toContain('Este mes va del 01/11/2026 al 01/12/2026.');
    expect(text()).toContain('40 de 300 facturas este mes');
    expect(button('Elegir')).toBeUndefined();

    button('Gestionar suscripción')!.click();
    http.expectOne(portalUrl).flush({ url: 'https://billing.stripe.com/p/session/1' });
    await fixture.whenStable();

    expect(redirect).toHaveBeenCalledWith('https://billing.stripe.com/p/session/1');
  });

  it('warns when the limit is close, and says why uploads stopped once it is reached', async () => {
    await render(basic({ uploadedInPeriod: 250 }));
    expect(text()).toContain('Te quedan 50 facturas.');

    fixture.destroy();
    await render(basic({ uploadedInPeriod: 300, uploadBlock: 'LimitReached' }));
    expect(text()).toContain('Has subido las 300 facturas de este mes.');
  });

  it('says when the trial ended', async () => {
    await render(trial({ periodEnd: '2026-10-31T10:00:00Z', uploadBlock: 'TrialEnded' }));

    expect(text()).toContain('La prueba terminó el 31/10/2026.');
    expect(text()).toContain('La prueba gratuita ha terminado. Elige un plan');
  });

  it('tells about a failed payment while everything keeps working', async () => {
    await render(basic({ status: 'PastDue' }));

    expect(text()).toContain('Pago pendiente');
    expect(text()).toContain('No se ha podido cobrar el último pago.');
    expect(button('Gestionar suscripción')).toBeTruthy();
  });

  it('shows members the plan, without the buttons to change it', async () => {
    role.set('Member');
    await render(trial());

    expect(button('Elegir Basic')).toBeUndefined();
    expect(text()).toContain('Solo el titular de la gestoría puede elegir o cambiar el plan.');
  });

  it('back from Checkout, looks again until Stripe confirms the plan', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await render(trial(), 'done');

    expect(text()).toContain('Pago recibido.');
    await waitForTimers();
    TestBed.tick();
    await respond(basic());

    expect(text()).not.toContain('Pago recibido.');
    expect(text()).toContain('Al día');
    expect(navigate).toHaveBeenCalledWith([], { queryParams: {}, replaceUrl: true });
  });

  it('back from Checkout, stops looking after a while and says Stripe is slow', async () => {
    await render(trial(), 'done');

    for (let attempt = 0; attempt < 2; attempt++) {
      await waitForTimers();
      TestBed.tick();
      await respond(trial());
    }

    expect(text()).not.toContain('Pago recibido.');
    expect(text()).toContain('Stripe aún no ha confirmado el pago.');
  });
});
