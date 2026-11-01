import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  DOCUMENT,
  InjectionToken,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { firstValueFrom, Observable } from 'rxjs';
import { CurrentUserService } from '../core/firm/current-user.service';
import {
  BillingRedirect,
  billingErrorMessage,
  checkoutUrl,
  isPaying,
  paidPlans,
  Plan,
  planNames,
  portalUrl,
  statusNames,
  Subscription,
  subscriptionUrl,
  uploadBlockMessage,
  usageLevel,
} from './subscription';

/** Sends the browser to one of Stripe's pages. A token, so tests don't leave the page. */
export const BILLING_REDIRECT = new InjectionToken<(url: string) => void>('BILLING_REDIRECT', {
  factory: () => {
    const location = inject(DOCUMENT).location;
    return (url) => location.assign(url);
  },
});

/** How often to look again after Checkout, and for how long, while Stripe's webhook arrives. */
export const CHECKOUT_POLL = new InjectionToken<{ intervalMs: number; attempts: number }>(
  'CHECKOUT_POLL',
  { factory: () => ({ intervalMs: 2000, attempts: 15 }) },
);

/**
 * The firm's plan, how much of it is used, and the way to Stripe to pay or change it (ADR 0012). Everyone in the firm
 * sees it; only the owner pays. Coming back from Checkout (?checkout=done) the plan may not have changed yet: the
 * webhook is what changes it, so the page looks again for a while.
 */
@Component({
  selector: 'app-billing-page',
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './billing-page.html',
  styleUrl: './billing-page.scss',
})
export class BillingPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly redirect = inject(BILLING_REDIRECT);
  private readonly poll = inject(CHECKOUT_POLL);
  private readonly currentUser = inject(CurrentUserService);

  /** "done" when Stripe sends the owner back after paying (query parameter). */
  readonly checkout = input<string>();

  protected readonly plans = paidPlans;
  protected readonly planNames = planNames;
  protected readonly statusNames = statusNames;

  protected readonly isOwner = computed(() => this.currentUser.firm()?.role === 'Owner');

  protected readonly subscription = httpResource<Subscription>(() => subscriptionUrl);

  /** The last loaded value, kept while it reloads, so polling doesn't make the page blink. */
  protected readonly current = computed(() =>
    this.subscription.hasValue() ? this.subscription.value() : undefined,
  );

  protected readonly level = computed(() => {
    const subscription = this.current();
    return subscription ? usageLevel(subscription) : 'ok';
  });

  protected readonly usedShare = computed(() => {
    const subscription = this.current();
    return subscription
      ? Math.min(100, (subscription.uploadedInPeriod / subscription.invoiceLimit) * 100)
      : 0;
  });

  protected readonly blockMessage = computed(() => {
    const subscription = this.current();
    return subscription ? uploadBlockMessage(subscription) : null;
  });

  protected readonly paying = computed(() => {
    const subscription = this.current();
    return subscription ? isPaying(subscription) : false;
  });

  /** Back from Checkout and Stripe hasn't told Plumy yet. */
  protected readonly waitingForStripe = signal(false);
  /** Back from Checkout, looked long enough, and still nothing. */
  protected readonly stripeIsSlow = signal(false);

  /** Which button is on its way to Stripe. */
  protected readonly busy = signal<Plan | 'portal' | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    let attempts = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));

    effect(() => {
      if (this.checkout() !== 'done' || this.subscription.isLoading()) {
        return;
      }
      const subscription = this.current();
      if (!subscription) {
        return;
      }

      if (isPaying(subscription)) {
        this.waitingForStripe.set(false);
        // Paid: drop ?checkout=done, so a reload doesn't wait again
        void this.router.navigate([], { queryParams: {}, replaceUrl: true });
        return;
      }

      if (attempts >= this.poll.attempts) {
        this.waitingForStripe.set(false);
        this.stripeIsSlow.set(true);
        return;
      }

      this.waitingForStripe.set(true);
      attempts++;
      clearTimeout(timer);
      timer = setTimeout(() => this.subscription.reload(), this.poll.intervalMs);
    });
  }

  protected async choose(plan: Plan): Promise<void> {
    await this.goToStripe(plan, () => this.http.post<BillingRedirect>(checkoutUrl, { plan }));
  }

  protected async openPortal(): Promise<void> {
    await this.goToStripe('portal', () => this.http.post<BillingRedirect>(portalUrl, null));
  }

  private async goToStripe(
    button: Plan | 'portal',
    request: () => Observable<BillingRedirect>,
  ): Promise<void> {
    this.busy.set(button);
    this.error.set(null);
    try {
      const { url } = await firstValueFrom(request());
      // Stays busy: the page is about to go
      this.redirect(url);
    } catch (error) {
      this.busy.set(null);
      this.error.set(billingErrorMessage(error));
      // A 409 means the plan changed under us: show the one there is now
      this.subscription.reload();
    }
  }
}
