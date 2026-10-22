import { DatePipe, DOCUMENT } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject, linkedSignal, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { formatFileSize } from '../../shared/file-size';
import { PagedResult } from '../../shared/paged-result';
import { provideSpanishPaginatorIntl } from '../../shared/spanish-paginator-intl';
import { Invoice, invoicesUrl } from '../invoice';
import { InvoicesApi } from '../invoices-api';
import { UploadInvoicesDialog } from '../upload-invoices-dialog/upload-invoices-dialog';

/** Every invoice of the firm, newest first. */
@Component({
  selector: 'app-invoice-list',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
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

  protected readonly displayedColumns = [
    'fileName',
    'clientCompanyName',
    'uploadedAt',
    'sizeBytes',
  ];
  protected readonly pageSizeOptions = [10, 20, 50, 100];
  protected readonly formatFileSize = formatFileSize;

  protected readonly pageSize = signal(20);
  /** 0-based, like mat-paginator. */
  protected readonly pageIndex = signal(0);

  protected readonly invoices = httpResource<PagedResult<Invoice>>(() => ({
    url: invoicesUrl,
    params: { page: this.pageIndex() + 1, pageSize: this.pageSize() },
  }));

  /** Last page received: keeps the table on screen while the next one loads. */
  protected readonly page = linkedSignal<
    PagedResult<Invoice> | undefined,
    PagedResult<Invoice> | undefined
  >({
    source: () => (this.invoices.hasValue() ? this.invoices.value() : undefined),
    computation: (current, previous) => current ?? previous?.value,
  });

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
