import { DatePipe, DOCUMENT } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, InjectionToken, linkedSignal, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { formatFileSize } from '../../shared/file-size';
import { formatMoney } from '../../shared/money';
import { PagedResult } from '../../shared/paged-result';
import { provideSpanishPaginatorIntl } from '../../shared/spanish-paginator-intl';
import {
  canRetryExtraction,
  describeIssues,
  extractionStatusLabels,
  Invoice,
  invoicesUrl,
  isExtractionInProgress,
  LedgerAccount,
  ledgerAccountsUrl,
} from '../invoice';
import { InvoicesApi } from '../invoices-api';
import { LedgerAccountCell } from '../ledger-account-cell/ledger-account-cell';
import { UploadInvoicesDialog } from '../upload-invoices-dialog/upload-invoices-dialog';

/** How often (ms) the list refreshes while the worker is reading some of its invoices. Tests shorten it. */
export const INVOICE_REFRESH_INTERVAL = new InjectionToken<number>('INVOICE_REFRESH_INTERVAL', {
  factory: () => 3000,
});

/** Every invoice of the firm, newest first. */
@Component({
  selector: 'app-invoice-list',
  imports: [
    DatePipe,
    LedgerAccountCell,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
    MatTooltipModule,
  ],
  templateUrl: './invoice-list.html',
  styleUrl: './invoice-list.scss',
  providers: [provideSpanishPaginatorIntl()],
})
export class InvoiceList {
  private readonly api = inject(InvoicesApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly document = inject(DOCUMENT);
  private readonly refreshInterval = inject(INVOICE_REFRESH_INTERVAL);

  protected readonly displayedColumns = [
    'fileName',
    'clientCompanyName',
    'supplier',
    'totalAmount',
    'ledgerAccount',
    'extractionStatus',
    'uploadedAt',
    'sizeBytes',
  ];
  protected readonly pageSizeOptions = [10, 20, 50, 100];
  protected readonly formatFileSize = formatFileSize;
  protected readonly formatMoney = formatMoney;

  /** Read again on every refresh, so a Pending invoice gets its retry button once it has waited too long. */
  private readonly now = signal(Date.now());
  /** Ids being retried right now: their button is disabled until the API answers. */
  protected readonly retrying = signal<ReadonlySet<string>>(new Set());

  protected readonly pageSize = signal(20);
  /** 0-based, like mat-paginator. */
  protected readonly pageIndex = signal(0);

  protected readonly invoices = httpResource<PagedResult<Invoice>>(() => ({
    url: invoicesUrl,
    params: { page: this.pageIndex() + 1, pageSize: this.pageSize() },
  }));

  /** The chart of accounts for the pickers. The same for every firm: read once. */
  protected readonly ledgerAccounts = httpResource<LedgerAccount[]>(() => ledgerAccountsUrl, {
    defaultValue: [],
  });

  /** Last page received: keeps the table on screen while the next one loads. */
  protected readonly page = linkedSignal<
    PagedResult<Invoice> | undefined,
    PagedResult<Invoice> | undefined
  >({
    source: () => (this.invoices.hasValue() ? this.invoices.value() : undefined),
    computation: (current, previous) => current ?? previous?.value,
  });

  constructor() {
    // While the worker is on some invoice of this page, refresh until it's done. The page itself decides:
    // nothing in progress, no timer
    effect((onCleanup) => {
      const page = this.page();
      if (!page?.items.some((invoice) => isExtractionInProgress(invoice, this.now()))) {
        return;
      }
      const timer = setTimeout(() => {
        this.now.set(Date.now());
        this.invoices.reload();
      }, this.refreshInterval);
      onCleanup(() => clearTimeout(timer));
    });
  }

  // A method, not the map in the template: the table's row variable is untyped there
  protected statusLabel(invoice: Invoice): string {
    return this.needsReview(invoice) ? 'Revisar' : extractionStatusLabels[invoice.extractionStatus];
  }

  /** Read, but the checks found something a person should look at. */
  protected needsReview(invoice: Invoice): boolean {
    return invoice.extractionStatus === 'Extracted' && invoice.issues.length > 0;
  }

  /** What the tooltip explains: why it failed, or what to review. */
  protected statusDetail(invoice: Invoice): string {
    return invoice.extractionError ?? describeIssues(invoice.issues);
  }

  protected canRetry(invoice: Invoice): boolean {
    return canRetryExtraction(invoice, this.now());
  }

  protected async retry(invoice: Invoice): Promise<void> {
    this.retrying.update((ids) => new Set(ids).add(invoice.id));
    try {
      await firstValueFrom(this.api.retryExtraction(invoice.id));
      this.snackBar.open('La factura se volverá a leer.', 'OK', { duration: 4000 });
    } catch (error) {
      // 409: someone else got there first (it's being read, or it's done). Either way the list is out of date
      const message =
        error instanceof HttpErrorResponse && error.status === 409
          ? 'La factura ya se está leyendo o ya está leída.'
          : 'No se ha podido volver a leer la factura.';
      this.snackBar.open(message, 'OK');
    } finally {
      this.retrying.update((ids) => {
        const next = new Set(ids);
        next.delete(invoice.id);
        return next;
      });
      this.now.set(Date.now());
      this.invoices.reload();
    }
  }

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  protected async openUpload(): Promise<void> {
    const dialogRef = this.dialog.open<UploadInvoicesDialog, void, number>(UploadInvoicesDialog, {
      width: '560px',
      // Only its own buttons close it: a stray click outside must not hide uploads in progress
      disableClose: true,
    });

    const uploaded = await firstValueFrom(dialogRef.afterClosed());
    if (!uploaded) {
      return;
    }

    this.snackBar.open(uploaded === 1 ? 'Factura subida.' : `${uploaded} facturas subidas.`, 'OK', {
      duration: 4000,
    });
    // The new invoices are the newest: back to the first page
    this.pageIndex.set(0);
    this.invoices.reload();
  }

  protected async download(invoice: Invoice): Promise<void> {
    try {
      const blob = await firstValueFrom(this.api.downloadFile(invoice.id));
      // A temporary link to the downloaded bytes, clicked by code so the file keeps its original name
      const url = URL.createObjectURL(blob);
      const link = this.document.createElement('a');
      link.href = url;
      link.download = invoice.fileName;
      link.click();
      // Freed on the next tick: revoking straight away can cancel the download in some browsers
      setTimeout(() => URL.revokeObjectURL(url));
    } catch {
      this.snackBar.open('No se ha podido descargar la factura.', 'OK');
    }
  }
}
