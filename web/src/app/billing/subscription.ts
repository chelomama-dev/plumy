import { HttpErrorResponse } from '@angular/common/http';

export type Plan = 'Trial' | 'Basic' | 'Pro';
export type SubscriptionStatus = 'Trialing' | 'Active' | 'PastDue' | 'Canceled';
export type UploadBlock = 'None' | 'TrialEnded' | 'LimitReached' | 'NotSubscribed';

/** Mirrors Plumy.Application.Billing.SubscriptionResponse. */
export interface Subscription {
  plan: Plan;
  status: SubscriptionStatus;
  /** ISO 8601: invoices uploaded from here on count. */
  periodStart: string;
  /** ISO 8601: when the trial ends, or when Stripe bills the next month. */
  periodEnd: string;
  invoiceLimit: number;
  uploadedInPeriod: number;
  uploadBlock: UploadBlock;
}

/** Mirrors Plumy.Application.Billing.BillingRedirectResponse: a page hosted by Stripe. */
export interface BillingRedirect {
  url: string;
}

export const subscriptionUrl = '/api/firm/subscription';
export const checkoutUrl = '/api/firm/billing/checkout';
export const portalUrl = '/api/firm/billing/portal';

/** The plans a firm can pay for. Same limits as Plumy.Domain.Billing.Plans; the prices live in Stripe. */
export const paidPlans = [
  { plan: 'Basic', name: 'Basic', invoiceLimit: 300, hint: 'Para una gestoría pequeña.' },
  { plan: 'Pro', name: 'Pro', invoiceLimit: 1500, hint: 'Para mucho volumen o muchas empresas.' },
] as const satisfies readonly { plan: Plan; name: string; invoiceLimit: number; hint: string }[];

export const planNames: Record<Plan, string> = {
  Trial: 'Prueba gratuita',
  Basic: 'Basic',
  Pro: 'Pro',
};

export const statusNames: Record<SubscriptionStatus, string> = {
  Trialing: 'En prueba',
  Active: 'Al día',
  PastDue: 'Pago pendiente',
  Canceled: 'Cancelado',
};

/** From this share of the limit on, the screens warn that it is running out. */
export const nearLimitShare = 0.8;

export type UsageLevel = 'ok' | 'near' | 'blocked';

/** How worried the screens should be: blocked beats near, whatever the count. */
export function usageLevel(subscription: Subscription): UsageLevel {
  if (subscription.uploadBlock !== 'None') {
    return 'blocked';
  }
  return subscription.uploadedInPeriod >= subscription.invoiceLimit * nearLimitShare
    ? 'near'
    : 'ok';
}

/** Whether the firm pays a plan in Stripe: changing it goes through the portal, not a second Checkout. */
export function isPaying(subscription: Subscription): boolean {
  return subscription.status === 'Active' || subscription.status === 'PastDue';
}

/** Why uploads stopped, in Spanish, or null if they didn't. */
export function uploadBlockMessage(subscription: Subscription): string | null {
  switch (subscription.uploadBlock) {
    case 'TrialEnded':
      return 'La prueba gratuita ha terminado. Elige un plan para seguir subiendo facturas.';
    case 'LimitReached':
      return isPaying(subscription)
        ? `Has subido las ${subscription.invoiceLimit} facturas de este mes. Cambia de plan para subir más.`
        : `Has subido las ${subscription.invoiceLimit} facturas de la prueba. Elige un plan para subir más.`;
    case 'NotSubscribed':
      return 'La gestoría no tiene un plan activo. Elige uno para seguir subiendo facturas.';
    default:
      return null;
  }
}

/** Spanish message for a failed trip to Stripe. The API's details are in English and meant for developers. */
export function billingErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 0:
        return 'No se ha podido conectar con el servidor.';
      case 409:
        return 'El plan ha cambiado mientras tanto. Revisa la pantalla e inténtalo de nuevo.';
      case 503:
        return 'Los pagos no están disponibles ahora mismo. Inténtalo más tarde.';
    }
  }
  return 'No se ha podido abrir la página de pago.';
}
