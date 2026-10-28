import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, linkedSignal, signal } from '@angular/core';
import {
  disabled,
  form,
  FormField,
  FormRoot,
  maxLength,
  pattern,
  TreeValidationResult,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { formatAmountInput, parseAmountInput } from '../../shared/money';
import { saveFile } from '../../shared/save-file';
import {
  canApprove,
  canReject,
  canRetryNow,
  ExtractedInvoiceData,
  extractionErrorMessage,
  extractionStatusLabels,
  Invoice,
  InvoiceField,
  invoiceIssueLabels,
  invoiceUrl,
  isEditable,
  isExtractionInProgress,
  LedgerAccount,
  ledgerAccountsUrl,
  lowConfidence,
  reviewStatusLabels,
} from '../invoice';
import { InvoiceActivity } from '../invoice-activity';
import { InvoicesApi } from '../invoices-api';
import { INVOICE_REFRESH_INTERVAL } from '../invoice-list/invoice-list';
import { LedgerAccountCell } from '../ledger-account-cell/ledger-account-cell';
import { RejectInvoiceDialog } from '../reject-invoice-dialog/reject-invoice-dialog';

/** The form edits text, like the inputs: amounts as "1234,56", the date as yyyy-MM-dd. */
interface ReviewModel {
  supplierName: string;
  supplierTaxId: string;
  customerTaxId: string;
  invoiceNumber: string;
  issueDate: string;
  netAmount: string;
  taxAmount: string;
  totalAmount: string;
  currency: string;
  itemsDescription: string;
}

/** Same limits as ExtractedInvoiceData in the domain. */
const limits = {
  supplierName: 200,
  taxId: 20,
  invoiceNumber: 50,
  itemsDescription: 500,
};

function toModel(data: ExtractedInvoiceData | null): ReviewModel {
  return {
    supplierName: data?.supplierName ?? '',
    supplierTaxId: data?.supplierTaxId ?? '',
    customerTaxId: data?.customerTaxId ?? '',
    invoiceNumber: data?.invoiceNumber ?? '',
    issueDate: data?.issueDate ?? '',
    netAmount: formatAmountInput(data?.netAmount),
    taxAmount: formatAmountInput(data?.taxAmount),
    totalAmount: formatAmountInput(data?.totalAmount),
    currency: data?.currency ?? '',
    itemsDescription: data?.itemsDescription ?? '',
  };
}

const text = (value: string) => (value.trim() === '' ? null : value.trim());

/** What the API gets: every field, an empty one as null. Only called once the form is valid. */
function toData(model: ReviewModel): ExtractedInvoiceData {
  return {
    supplierName: text(model.supplierName),
    supplierTaxId: text(model.supplierTaxId),
    customerTaxId: text(model.customerTaxId),
    invoiceNumber: text(model.invoiceNumber),
    issueDate: text(model.issueDate),
    netAmount: parseAmountInput(model.netAmount) ?? null,
    taxAmount: parseAmountInput(model.taxAmount) ?? null,
    totalAmount: parseAmountInput(model.totalAmount) ?? null,
    currency: text(model.currency)?.toUpperCase() ?? null,
    itemsDescription: text(model.itemsDescription),
  };
}

const sameData = (a: ExtractedInvoiceData | null, b: ExtractedInvoiceData | null) =>
  JSON.stringify(a) === JSON.stringify(b);

/** What a field says under it: a person already changed it, or the reader isn't sure of it. */
export interface FieldHint {
  kind: 'corrected' | 'doubtful';
  text: string;
}

/**
 * One invoice, to check before it's booked: the document on one side, what was read on the other. A person
 * corrects what's wrong, confirms the account and approves it, or rejects it.
 */
@Component({
  selector: 'app-invoice-review',
  imports: [
    DatePipe,
    FormField,
    FormRoot,
    LedgerAccountCell,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
  ],
  templateUrl: './invoice-review.html',
  styleUrl: './invoice-review.scss',
})
export class InvoiceReview {
  private readonly api = inject(InvoicesApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly document = inject(DOCUMENT);
  private readonly activity = inject(InvoiceActivity);
  private readonly refreshInterval = inject(INVOICE_REFRESH_INTERVAL);

  /** From the route (invoices/:id). */
  readonly id = input.required<string>();

  private readonly loaded = httpResource<Invoice>(() => invoiceUrl(this.id()));

  protected readonly ledgerAccounts = httpResource<LedgerAccount[]>(() => ledgerAccountsUrl, {
    defaultValue: [],
  });

  /** The invoice as last loaded or returned by a change: every answer of the API replaces it. */
  protected readonly invoice = linkedSignal<Invoice | undefined>(() =>
    this.loaded.hasValue() ? this.loaded.value() : undefined,
  );

  /**
   * Only the data, compared by value: a new account (a new invoice object, same data) must not reset what the
   * person is typing.
   */
  private readonly data = computed(() => this.invoice()?.extractedData ?? null, {
    equal: sameData,
  });

  private readonly model = linkedSignal(() => toModel(this.data()));

  protected readonly reviewForm = form(
    this.model,
    (invoice) => {
      // Approved, rejected or not read: nothing to edit. On the root, so every field follows
      disabled(invoice, () => !this.editable());
      maxLength(invoice.supplierName, limits.supplierName, { message: 'Demasiado largo.' });
      maxLength(invoice.supplierTaxId, limits.taxId, { message: 'Demasiado largo.' });
      maxLength(invoice.customerTaxId, limits.taxId, { message: 'Demasiado largo.' });
      maxLength(invoice.invoiceNumber, limits.invoiceNumber, { message: 'Demasiado largo.' });
      maxLength(invoice.itemsDescription, limits.itemsDescription, { message: 'Demasiado largo.' });
      maxLength(invoice.currency, 3, { message: 'Código de 3 letras, como EUR.' });
      pattern(invoice.currency, /^([A-Za-z]{3})?$/, {
        message: 'Código de 3 letras, como EUR.',
      });
      for (const field of [invoice.netAmount, invoice.taxAmount, invoice.totalAmount]) {
        validate(field, ({ value }) =>
          parseAmountInput(value()) === undefined
            ? { kind: 'amount', message: 'Escribe un importe, como 1234,56.' }
            : undefined,
        );
      }
    },
    { submission: { action: () => this.save() } },
  );

  /** The form differs from what is stored: approving now would approve something else than what's on screen. */
  protected readonly dirty = computed(
    () => !sameData(toData(this.model()), toData(toModel(this.data()))),
  );

  protected readonly editable = computed(() => {
    const invoice = this.invoice();
    return !!invoice && isEditable(invoice);
  });

  protected readonly canApprove = computed(() => {
    const invoice = this.invoice();
    return !!invoice && canApprove(invoice) && !this.dirty();
  });

  /** Why the approve button is off, for its tooltip. */
  protected readonly approveHint = computed(() => {
    const invoice = this.invoice();
    if (!invoice || this.canApprove()) {
      return '';
    }
    if (this.dirty()) {
      return 'Guarda los cambios antes de aprobarla.';
    }
    return invoice.ledgerAccount ? '' : 'Confirma la cuenta antes de aprobarla.';
  });

  protected readonly canReject = computed(() => {
    const invoice = this.invoice();
    return !!invoice && canReject(invoice);
  });

  protected readonly issues = computed(() =>
    (this.invoice()?.issues ?? []).map((issue) => invoiceIssueLabels[issue] ?? issue),
  );

  protected readonly deciding = signal(false);

  /** The file, shown next to the form. Its own object URL, released when it changes or the page closes. */
  protected readonly preview = signal<{ url: SafeResourceUrl; isPdf: boolean } | null>(null);
  protected readonly previewError = signal(false);

  protected readonly reviewStatusLabels = reviewStatusLabels;
  protected readonly extractionStatusLabels = extractionStatusLabels;
  protected readonly extractionErrorMessage = extractionErrorMessage;

  /** Queueing it again right now: the button waits for the API. */
  protected readonly retrying = signal(false);

  /** Failed (or stuck in the queue) and still undecided: it can be read again from here. */
  protected readonly canRetry = computed(() => {
    const invoice = this.invoice();
    return !!invoice && canRetryNow(invoice, Date.now());
  });

  constructor() {
    // While the AI reads it or picks its account, look again until it's done: no "come back in a few seconds".
    // Only the data is compared to reset the form, so a refresh never wipes what the person is typing
    effect((onCleanup) => {
      const invoice = this.invoice();
      if (!invoice || !isExtractionInProgress(invoice, Date.now())) {
        return;
      }
      const timer = setTimeout(() => this.loaded.reload(), this.refreshInterval);
      onCleanup(() => clearTimeout(timer));
    });

    effect((onCleanup) => {
      const id = this.id();
      let objectUrl: string | null = null;
      let cancelled = false;
      this.preview.set(null);
      this.previewError.set(false);

      firstValueFrom(this.api.downloadFile(id)).then(
        (blob) => {
          if (cancelled) {
            return;
          }
          objectUrl = URL.createObjectURL(blob);
          this.preview.set({
            // Our own blob: URL, made from the file the API served with nosniff: safe to frame
            url: this.sanitizer.bypassSecurityTrustResourceUrl(objectUrl),
            isPdf: blob.type === 'application/pdf',
          });
        },
        () => !cancelled && this.previewError.set(true),
      );

      onCleanup(() => {
        cancelled = true;
        if (objectUrl) {
          URL.revokeObjectURL(objectUrl);
        }
      });
    });
  }

  protected hint(field: InvoiceField): FieldHint | null {
    const invoice = this.invoice();
    if (!invoice) {
      return null;
    }
    if (invoice.correctedFields.includes(field)) {
      return { kind: 'corrected', text: 'Corregido a mano' };
    }
    const confidence = lowConfidence(invoice, field);
    return confidence === null
      ? null
      : {
          kind: 'doubtful',
          // Short: it sits under a narrow field (an amount) and must fit on one line, so the percent sign keeps to
          // its number (no-break space). The help icon and the colour say the rest
          text: `Dudosa (${Math.round(confidence * 100)}\u00a0%)`,
        };
  }

  /** The account cell saved a new account: the rest of the invoice is as it was, edits included. */
  protected accountChanged(invoice: Invoice): void {
    this.invoice.set(invoice);
  }

  private async save(): Promise<TreeValidationResult> {
    const invoice = this.invoice();
    if (!invoice) {
      return undefined;
    }
    try {
      const saved = await firstValueFrom(this.api.correctData(invoice.id, toData(this.model())));
      this.invoice.set(saved);
      this.snackBar.open('Cambios guardados.', 'OK', { duration: 3000 });
      return undefined;
    } catch (error) {
      return {
        kind: 'server',
        message: this.errorMessage(error, 'No se han podido guardar los cambios.'),
      };
    }
  }

  protected async approve(): Promise<void> {
    await this.decide('Approved', undefined, 'Factura aprobada.');
  }

  protected async reject(): Promise<void> {
    const reason = await firstValueFrom(
      this.dialog
        .open<RejectInvoiceDialog, void, string>(RejectInvoiceDialog, { width: '480px' })
        .afterClosed(),
    );
    if (reason) {
      await this.decide('Rejected', reason, 'Factura rechazada.');
    }
  }

  protected async reopen(): Promise<void> {
    await this.decide('Unreviewed', undefined, 'Factura reabierta: ya puedes cambiarla.');
  }

  private async decide(
    status: 'Approved' | 'Rejected' | 'Unreviewed',
    reason: string | undefined,
    done: string,
  ): Promise<void> {
    const invoice = this.invoice();
    if (!invoice) {
      return;
    }
    this.deciding.set(true);
    try {
      this.invoice.set(await firstValueFrom(this.api.review(invoice.id, status, reason)));
      this.snackBar.open(done, 'OK', { duration: 3000 });
    } catch (error) {
      this.snackBar.open(this.errorMessage(error, 'No se ha podido guardar la revisión.'), 'OK');
      // Someone else may have changed it: show it as it is now
      this.loaded.reload();
    } finally {
      this.deciding.set(false);
    }
  }

  /** The newest invoice still to review, other than this one; back to the list when there are none. */
  protected async next(): Promise<void> {
    try {
      const page = await firstValueFrom(
        this.api.list({ reviewStatus: 'Unreviewed', page: 1, pageSize: 2 }),
      );
      const next = page.items.find((item) => item.id !== this.id());
      await this.router.navigate(next ? ['/invoices', next.id] : ['/invoices']);
      if (!next) {
        this.snackBar.open('No quedan facturas por revisar.', 'OK', { duration: 3000 });
      }
    } catch {
      this.snackBar.open('No se ha podido buscar la siguiente factura.', 'OK');
    }
  }

  protected async download(): Promise<void> {
    const invoice = this.invoice();
    if (!invoice) {
      return;
    }
    try {
      saveFile(
        this.document,
        await firstValueFrom(this.api.downloadFile(invoice.id)),
        invoice.fileName,
      );
    } catch {
      this.snackBar.open('No se ha podido descargar la factura.', 'OK');
    }
  }

  protected reload(): void {
    this.loaded.reload();
  }

  protected async retry(): Promise<void> {
    const invoice = this.invoice();
    if (!invoice) {
      return;
    }
    this.retrying.set(true);
    try {
      const queued = await this.activity.retry(invoice);
      if (queued) {
        this.invoice.set(queued);
      } else {
        this.loaded.reload();
      }
    } finally {
      this.retrying.set(false);
    }
  }

  protected readonly loadError = computed(() => this.loaded.error());

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) {
        return 'Alguien ha cambiado la factura mientras tanto. Revisa cómo está ahora.';
      }
      if (error.status === 400 && typeof error.error?.detail === 'string') {
        return error.error.detail;
      }
    }
    return fallback;
  }
}
