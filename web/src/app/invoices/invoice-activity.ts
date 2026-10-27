import { HttpErrorResponse } from '@angular/common/http';
import {
  computed,
  DestroyRef,
  inject,
  Injectable,
  InjectionToken,
  Injector,
  signal,
} from '@angular/core';
import type { MatSnackBar, MatSnackBarConfig } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  canRetryNow,
  extractionErrorMessage,
  Invoice,
  isExtractionInProgress,
  isSuggestingAccount,
} from './invoice';
import { InvoicesApi } from './invoices-api';

/** How often (ms) the invoices being read are checked. Tests shorten it. */
export const INVOICE_ACTIVITY_INTERVAL = new InjectionToken<number>('INVOICE_ACTIVITY_INTERVAL', {
  factory: () => 3000,
});

/** What the AI is doing with an invoice right now, for the header and the list. */
export type InvoicePhase = 'queued' | 'reading' | 'suggesting';

export function invoicePhase(invoice: Invoice): InvoicePhase {
  if (invoice.extractionStatus === 'Processing') {
    return 'reading';
  }
  return isSuggestingAccount(invoice) ? 'suggesting' : 'queued';
}

/**
 * Follows the invoices this person uploaded or retried until the AI is done with them, wherever they are in the
 * app, and says how it went: read (and the account suggested, or not), or failed, with a retry at hand.
 *
 * Only this session's own invoices: someone else's upload isn't news to this person. A reload forgets them; the
 * list still shows where each invoice stands.
 */
@Injectable({ providedIn: 'root' })
export class InvoiceActivity {
  private readonly api = inject(InvoicesApi);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  private readonly interval = inject(INVOICE_ACTIVITY_INTERVAL);

  private readonly watched = signal<ReadonlyMap<string, Invoice>>(new Map());
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** The invoices the AI is still working on, oldest first. */
  readonly active = computed(() => [...this.watched().values()]);

  /**
   * Bumped each time some watched invoice is done: pages showing invoices reload then, so they don't wait for
   * their own next refresh.
   */
  readonly finished = signal(0);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  /** Starts following these invoices (just uploaded or queued again). Already finished ones are ignored. */
  watch(invoices: readonly Invoice[]): void {
    const now = Date.now();
    const inProgress = invoices.filter((invoice) => isExtractionInProgress(invoice, now));
    if (inProgress.length === 0) {
      return;
    }
    this.watched.update((map) => {
      const next = new Map(map);
      for (const invoice of inProgress) {
        next.set(invoice.id, invoice);
      }
      return next;
    });
    this.schedule();
  }

  /** Queues a failed (or stuck) invoice again and follows it. Says so if it can't. */
  async retry(invoice: Invoice): Promise<Invoice | null> {
    try {
      const queued = await firstValueFrom(this.api.retryExtraction(invoice.id));
      this.watch([queued]);
      void this.notify(`Volviendo a leer «${invoice.fileName}»…`, undefined, { duration: 3000 });
      return queued;
    } catch (error) {
      // 409: just queued, being read, or done (someone else got there first). Either way, nothing to do
      const message =
        error instanceof HttpErrorResponse && error.status === 409
          ? 'La factura ya está en cola o leyéndose, o ya se ha leído.'
          : 'No se ha podido volver a leer la factura.';
      void this.notify(message, 'OK');
      return null;
    }
  }

  private schedule(): void {
    if (this.timer !== undefined || this.watched().size === 0) {
      return;
    }
    this.timer = setTimeout(() => {
      this.timer = undefined;
      void this.check();
    }, this.interval);
  }

  private async check(): Promise<void> {
    const now = Date.now();
    const done: Invoice[] = [];
    const ids = [...this.watched().keys()];

    await Promise.all(
      ids.map(async (id) => {
        let latest: Invoice;
        try {
          latest = await firstValueFrom(this.api.get(id));
        } catch (error) {
          // Gone (deleted, or the person switched firms): nothing more to say. Anything else: next round
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.forget(id);
          }
          return;
        }
        if (isExtractionInProgress(latest, now)) {
          this.watched.update((map) => (map.has(id) ? new Map(map).set(id, latest) : map));
        } else {
          this.forget(id);
          done.push(latest);
        }
      }),
    );

    if (done.length > 0) {
      this.finished.update((n) => n + 1);
      this.announce(done, now);
    }
    this.schedule();
  }

  private forget(id: string): void {
    this.watched.update((map) => {
      if (!map.has(id)) {
        return map;
      }
      const next = new Map(map);
      next.delete(id);
      return next;
    });
  }

  private announce(done: readonly Invoice[], now: number): void {
    if (done.length > 1) {
      const failed = done.filter((invoice) => invoice.extractionStatus !== 'Extracted').length;
      const message =
        failed === 0
          ? `${done.length} facturas leídas.`
          : `${done.length - failed} de ${done.length} facturas leídas; ${failed} con error.`;
      void this.notify(message, 'Ver', { duration: 8000 }, () =>
        this.router.navigate(['/invoices']),
      );
      return;
    }

    const [invoice] = done;
    if (invoice.extractionStatus === 'Extracted') {
      void this.notify(
        `«${invoice.fileName}» leída. ${suggestionOutcome(invoice)}`,
        'Revisar',
        { duration: 8000 },
        () => this.router.navigate(['/invoices', invoice.id]),
      );
      return;
    }

    // Failed, or still waiting in the queue for too long: both can be queued again
    const message =
      invoice.extractionStatus === 'Failed'
        ? `No se ha podido leer «${invoice.fileName}». ${extractionErrorMessage(invoice)}`
        : `«${invoice.fileName}» sigue en cola. Puede que no se haya enviado a la IA.`;
    const canRetry = canRetryNow(invoice, now);
    void this.notify(
      message,
      canRetry ? 'Reintentar' : 'OK',
      { panelClass: 'snack-error' },
      canRetry ? () => this.retry(invoice) : undefined,
    );
  }

  /**
   * The snack bar is loaded the first time there is something to say: this service lives in the header, which is
   * in the initial bundle, and the snack bar (with the overlay it needs) would add ~70 kB to every first load.
   */
  private async notify(
    message: string,
    action?: string,
    config?: MatSnackBarConfig,
    onAction?: () => unknown,
  ): Promise<void> {
    const { MatSnackBar } = await import('@angular/material/snack-bar');
    const snackBar: MatSnackBar = this.injector.get(MatSnackBar);
    const ref = snackBar.open(message, action, config);
    if (onAction) {
      ref.onAction().subscribe(() => void onAction());
    }
  }
}

/** The second half of the "read" message: what came of the account. */
export function suggestionOutcome(invoice: Invoice): string {
  if (invoice.ledgerAccount) {
    return `Cuenta ${invoice.ledgerAccount.code} confirmada.`;
  }
  if (invoice.ledgerAccountSuggestion) {
    const { code, name } = invoice.ledgerAccountSuggestion;
    return `Cuenta sugerida: ${code} ${name}.`;
  }
  switch (invoice.suggestionStatus) {
    case 'NoSuggestion':
      return 'La IA no ha sugerido cuenta.';
    case 'Failed':
      return 'No se pudo sugerir cuenta.';
    default:
      return '';
  }
}
