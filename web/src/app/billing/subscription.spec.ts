import { HttpErrorResponse } from '@angular/common/http';
import {
  billingErrorMessage,
  isPaying,
  Subscription,
  uploadBlockMessage,
  usageLevel,
} from './subscription';

const subscription = (overrides: Partial<Subscription> = {}): Subscription => ({
  plan: 'Basic',
  status: 'Active',
  periodStart: '2026-11-01T00:00:00Z',
  periodEnd: '2026-12-01T00:00:00Z',
  invoiceLimit: 300,
  uploadedInPeriod: 0,
  uploadBlock: 'None',
  ...overrides,
});

describe('usageLevel', () => {
  it('is fine below 80 % of the limit', () => {
    expect(usageLevel(subscription({ uploadedInPeriod: 239 }))).toBe('ok');
  });

  it('warns from 80 % on', () => {
    expect(usageLevel(subscription({ uploadedInPeriod: 240 }))).toBe('near');
  });

  it('is blocked whenever uploads stopped, however few were uploaded', () => {
    expect(usageLevel(subscription({ uploadBlock: 'TrialEnded', uploadedInPeriod: 3 }))).toBe(
      'blocked',
    );
  });
});

describe('isPaying', () => {
  it('counts a failed payment Stripe is retrying, not the trial nor a canceled plan', () => {
    expect(isPaying(subscription({ status: 'PastDue' }))).toBe(true);
    expect(isPaying(subscription({ plan: 'Trial', status: 'Trialing' }))).toBe(false);
    expect(isPaying(subscription({ status: 'Canceled' }))).toBe(false);
  });
});

describe('uploadBlockMessage', () => {
  it('says why uploads stopped', () => {
    expect(uploadBlockMessage(subscription({ uploadBlock: 'TrialEnded' }))).toContain(
      'La prueba gratuita ha terminado',
    );
    expect(
      uploadBlockMessage(subscription({ uploadBlock: 'LimitReached', uploadedInPeriod: 300 })),
    ).toBe('Has subido las 300 facturas de este mes. Cambia de plan para subir más.');
    expect(
      uploadBlockMessage(
        subscription({
          plan: 'Trial',
          status: 'Trialing',
          invoiceLimit: 50,
          uploadBlock: 'LimitReached',
        }),
      ),
    ).toBe('Has subido las 50 facturas de la prueba. Elige un plan para subir más.');
    expect(uploadBlockMessage(subscription({ uploadBlock: 'NotSubscribed' }))).toContain(
      'no tiene un plan activo',
    );
  });

  it('is null while uploads work', () => {
    expect(uploadBlockMessage(subscription())).toBeNull();
  });
});

describe('billingErrorMessage', () => {
  it('explains the errors a person can do something about', () => {
    expect(billingErrorMessage(new HttpErrorResponse({ status: 503 }))).toContain(
      'Los pagos no están disponibles',
    );
    expect(billingErrorMessage(new HttpErrorResponse({ status: 409 }))).toContain(
      'El plan ha cambiado',
    );
    expect(billingErrorMessage(new Error('boom'))).toBe('No se ha podido abrir la página de pago.');
  });
});
