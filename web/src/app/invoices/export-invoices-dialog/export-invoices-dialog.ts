import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { firstValueFrom } from 'rxjs';
import { ClientCompany } from '../../client-companies/client-company';
import { ClientCompanyPicker } from '../../client-companies/client-company-picker/client-company-picker';
import { fileNameFromContentDisposition, saveFile } from '../../shared/save-file';
import { InvoiceExportFormat } from '../invoice';
import { InvoicesApi } from '../invoices-api';

/** Spanish message for a failed export. */
export function exportErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 0:
        return 'No se ha podido conectar con el servidor.';
      // The range is checked here first, so a 400 is the size limit
      case 400:
        return 'Hay demasiadas facturas en ese periodo. Elige un periodo más corto o una sola empresa.';
      case 404:
        return 'La empresa ya no existe.';
    }
  }
  return 'No se ha podido exportar.';
}

/**
 * Downloads the read invoices of one company (or all) in a period, as Excel or CSV, to import into the accounting
 * software. Closes with true once the file is saved.
 */
@Component({
  selector: 'app-export-invoices-dialog',
  imports: [
    ClientCompanyPicker,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatRadioModule,
  ],
  templateUrl: './export-invoices-dialog.html',
  styleUrl: './export-invoices-dialog.scss',
})
export class ExportInvoicesDialog {
  private readonly api = inject(InvoicesApi);
  private readonly document = inject(DOCUMENT);
  private readonly dialogRef = inject<MatDialogRef<ExportInvoicesDialog, boolean>>(MatDialogRef);

  /** None: every company of the firm. */
  protected readonly company = signal<ClientCompany | undefined>(undefined);
  /** yyyy-MM-dd, as date inputs give it; empty for no limit. */
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly format = signal<InvoiceExportFormat>('xlsx');

  protected readonly exporting = signal(false);
  protected readonly error = signal<string | null>(null);

  // yyyy-MM-dd strings compare like the dates they are
  protected readonly rangeError = computed(() =>
    this.from() && this.to() && this.from() > this.to()
      ? 'La fecha de inicio es posterior a la de fin.'
      : null,
  );

  protected readonly canExport = computed(() => !this.exporting() && !this.rangeError());

  protected async export(): Promise<void> {
    if (!this.canExport()) {
      return;
    }
    this.exporting.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.api.export({
          clientCompanyId: this.company()?.id,
          from: this.from(),
          to: this.to(),
          format: this.format(),
        }),
      );
      const fileName =
        fileNameFromContentDisposition(response.headers.get('Content-Disposition')) ??
        `facturas.${this.format()}`;
      saveFile(this.document, response.body!, fileName);
      this.dialogRef.close(true);
    } catch (error) {
      this.error.set(exportErrorMessage(error));
    } finally {
      this.exporting.set(false);
    }
  }
}
