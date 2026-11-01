import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Subscription, subscriptionUrl, uploadBlockMessage, usageLevel } from '../subscription';

/**
 * A line above the invoices when the plan is running out or has stopped uploads, with the way to the plan page.
 * Nothing at all while there is room, or if the plan can't be read: it is a warning, not a page of its own.
 */
@Component({
  selector: 'app-plan-usage-banner',
  imports: [MatButtonModule, MatIconModule, RouterLink],
  template: `
    @if (warning(); as warning) {
      <div
        class="banner"
        [class]="warning.level"
        [attr.role]="warning.level === 'blocked' ? 'alert' : 'status'"
      >
        <mat-icon aria-hidden="true">{{
          warning.level === 'blocked' ? 'block' : 'warning'
        }}</mat-icon>
        <span>{{ warning.message }}</span>
        <a matButton routerLink="/billing">Ver plan</a>
      </div>
    }
  `,
  styles: `
    @use '../../shared/ui';

    :host {
      display: block;
    }

    .banner {
      margin-bottom: 16px;

      &.near {
        @include ui.banner(var(--mat-sys-tertiary-container), var(--mat-sys-on-tertiary-container));
      }

      &.blocked {
        @include ui.banner(var(--mat-sys-error-container), var(--mat-sys-on-error-container));
      }

      span {
        flex: 1;
      }
    }
  `,
})
export class PlanUsageBanner {
  private readonly subscription = httpResource<Subscription>(() => subscriptionUrl);

  protected readonly warning = computed(() => {
    if (!this.subscription.hasValue()) {
      return null;
    }
    const subscription = this.subscription.value();
    const level = usageLevel(subscription);
    switch (level) {
      case 'blocked':
        return { level, message: uploadBlockMessage(subscription)! };
      case 'near': {
        const left = subscription.invoiceLimit - subscription.uploadedInPeriod;
        return {
          level,
          message: `Quedan ${left} de las ${subscription.invoiceLimit} facturas del plan en este periodo.`,
        };
      }
      default:
        return null;
    }
  });

  /** Reads the plan again: after uploading, the count has moved. */
  reload(): void {
    this.subscription.reload();
  }
}
