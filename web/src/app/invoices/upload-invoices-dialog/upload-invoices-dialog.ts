import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { debounceTime, distinctUntilChanged, firstValueFrom, map } from 'rxjs';
import { ClientCompany, clientCompaniesUrl } from '../../client-companies/client-company';
import { formatFileSize } from '../../shared/file-size';
import { PagedResult } from '../../shared/paged-result';
import { acceptedInvoiceFiles, checkInvoiceFile } from '../invoice';
import { InvoicesApi } from '../invoices-api';

export const companySearchDebounceMs = 300;

export type UploadStatus = 'pending' | 'uploading' | 'done' | 'error';

export interface UploadItem {
  id: number;
  file: File;
  status: UploadStatus;
  error?: string;
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
 * its own result. Closes with how many were uploaded.
 */
@Component({
  selector: 'app-upload-invoices-dialog',
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './upload-invoices-dialog.html',
  styleUrl: './upload-invoices-dialog.scss',
})
export class UploadInvoicesDialog {
  private readonly api = inject(InvoicesApi);
  private readonly dialogRef = inject<MatDialogRef<UploadInvoicesDialog, number>>(MatDialogRef);

  protected readonly accept = acceptedInvoiceFiles;
  protected readonly formatFileSize = formatFileSize;

  // ---- Client company: typed search against the API, a gestoría can have hundreds ----

  protected readonly companySearch = signal('');

  private readonly debouncedSearch = toSignal(
    toObservable(this.companySearch).pipe(
      map((text) => text.trim()),
      debounceTime(companySearchDebounceMs),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  protected readonly companies = httpResource<PagedResult<ClientCompany>>(() => ({
    url: clientCompaniesUrl,
    params: {
      page: 1,
      pageSize: 10,
      ...(this.debouncedSearch() ? { search: this.debouncedSearch() } : {}),
    },
  }));

  /** Keeps the previous options while a new search loads, so the panel doesn't flicker shut on every keystroke. */
  protected readonly companyOptions = linkedSignal<ClientCompany[] | undefined, ClientCompany[]>({
    source: () => (this.companies.hasValue() ? this.companies.value().items : undefined),
    computation: (current, previous) => current ?? previous?.value ?? [],
  });

  protected readonly company = signal<ClientCompany | undefined>(undefined);

  /** What the input shows once an option is picked. While typing it receives the text itself. */
  protected readonly displayCompany = (value: ClientCompany | string | null) =>
    typeof value === 'string' ? value : (value?.name ?? '');

  protected onCompanyInput(text: string): void {
    this.companySearch.set(text);
    // Typing again means looking for another company: the previous choice no longer matches the text
    this.company.set(undefined);
  }

  // ---- Files ----

  private nextId = 0;

  protected readonly items = signal<UploadItem[]>([]);
  protected readonly uploading = signal(false);
  protected readonly dragging = signal(false);

  protected readonly pendingCount = computed(
    () => this.items().filter((item) => item.status === 'pending').length,
  );
  protected readonly uploadedCount = computed(
    () => this.items().filter((item) => item.status === 'done').length,
  );
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
          await firstValueFrom(this.api.upload(company.id, item.file));
          this.setStatus(item, 'done');
        } catch (error) {
          this.setStatus(item, 'error', uploadErrorMessage(error));
        }
      }
    } finally {
      this.uploading.set(false);
    }
  }

  protected close(): void {
    this.dialogRef.close(this.uploadedCount());
  }

  private setStatus(item: UploadItem, status: UploadStatus, error?: string): void {
    this.items.update((items) =>
      items.map((i) => (i.id === item.id ? { ...i, status, error } : i)),
    );
  }
}
