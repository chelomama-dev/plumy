import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { ClientCompany } from '../../client-companies/client-company';
import { ClientCompanyPicker } from '../../client-companies/client-company-picker/client-company-picker';
import { formatFileSize } from '../../shared/file-size';
import { acceptedInvoiceFiles, checkInvoiceFile, Invoice } from '../invoice';
import { InvoicesApi } from '../invoices-api';

export type UploadStatus = 'pending' | 'uploading' | 'done' | 'error';

export interface UploadItem {
  id: number;
  file: File;
  status: UploadStatus;
  error?: string;
  /** The invoice the API created, once uploaded. */
  invoice?: Invoice;
}

/** Spanish message for a failed upload. The API's details are in English and meant for developers. */
export function uploadErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 0:
        return 'No se ha podido conectar con el servidor.';
      case 400:
        return 'No es un PDF, JPG o PNG válido de hasta 10 MB.';
      case 404:
        return 'La empresa ya no existe o está archivada.';
      case 402:
        return 'El plan no permite subir más facturas ahora. Revísalo en «Plan».';
      case 409:
        return 'Ya se había subido este archivo para esta empresa.';
      case 413:
        return 'Supera el máximo de 10 MB.';
    }
  }
  return 'No se ha podido subir.';
}

/**
 * Uploads one or more invoices of a client company, one request per file and one at a time, so each file gets
 * its own result. Closes with the invoices uploaded, so the AI's work on them can be followed.
 */
@Component({
  selector: 'app-upload-invoices-dialog',
  imports: [
    ClientCompanyPicker,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './upload-invoices-dialog.html',
  styleUrl: './upload-invoices-dialog.scss',
})
export class UploadInvoicesDialog {
  private readonly api = inject(InvoicesApi);
  private readonly dialogRef = inject<MatDialogRef<UploadInvoicesDialog, Invoice[]>>(MatDialogRef);

  protected readonly accept = acceptedInvoiceFiles;
  protected readonly formatFileSize = formatFileSize;

  /** Chosen with the picker, which searches the API as the user types. */
  protected readonly company = signal<ClientCompany | undefined>(undefined);

  // ---- Files ----

  private nextId = 0;

  protected readonly items = signal<UploadItem[]>([]);
  protected readonly uploading = signal(false);
  protected readonly dragging = signal(false);

  protected readonly pendingCount = computed(
    () => this.items().filter((item) => item.status === 'pending').length,
  );
  private readonly uploaded = computed(() =>
    this.items().flatMap((item) => (item.status === 'done' && item.invoice ? [item.invoice] : [])),
  );
  protected readonly uploadedCount = computed(() => this.uploaded().length);
  protected readonly canUpload = computed(
    () => !!this.company() && !this.uploading() && this.pendingCount() > 0,
  );

  protected addFiles(files: FileList | null | undefined): void {
    if (!files?.length) {
      return;
    }
    const added = Array.from(files).map((file): UploadItem => {
      // Obvious mistakes are flagged right away instead of after a round trip
      const error = checkInvoiceFile(file);
      return {
        id: this.nextId++,
        file,
        status: error ? 'error' : 'pending',
        error: error ?? undefined,
      };
    });
    this.items.update((items) => [...items, ...added]);
  }

  protected remove(item: UploadItem): void {
    this.items.update((items) => items.filter((i) => i !== item));
  }

  protected onDragOver(event: DragEvent): void {
    // Without preventDefault the browser refuses the drop (and would open the file instead)
    event.preventDefault();
    this.dragging.set(!this.uploading());
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (!this.uploading()) {
      this.addFiles(event.dataTransfer?.files);
    }
  }

  // ---- Upload ----

  protected async upload(): Promise<void> {
    const company = this.company();
    if (!company || this.uploading()) {
      return;
    }

    this.uploading.set(true);
    try {
      for (const item of this.items().filter((i) => i.status === 'pending')) {
        this.setStatus(item, 'uploading');
        try {
          const invoice = await firstValueFrom(this.api.upload(company.id, item.file));
          this.setStatus(item, 'done', undefined, invoice);
        } catch (error) {
          this.setStatus(item, 'error', uploadErrorMessage(error));
        }
      }
    } finally {
      this.uploading.set(false);
    }
  }

  protected close(): void {
    this.dialogRef.close(this.uploaded());
  }

  private setStatus(
    item: UploadItem,
    status: UploadStatus,
    error?: string,
    invoice?: Invoice,
  ): void {
    this.items.update((items) =>
      items.map((i) => (i.id === item.id ? { ...i, status, error, invoice } : i)),
    );
  }
}
