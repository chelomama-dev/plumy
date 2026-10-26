import { Component, inject, signal } from '@angular/core';
import { form, FormField, FormRoot, maxLength, required, validate } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { maxRejectionReasonLength } from '../invoice';

/** The usual reasons, one click away. Any other can be typed. */
export const commonRejectionReasons = [
  'No es una factura',
  'Duplicada',
  'Es de otra empresa',
] as const;

/** Asks why the invoice won't be booked. Closes with the reason; undefined when cancelled. */
@Component({
  selector: 'app-reject-invoice-dialog',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './reject-invoice-dialog.html',
  styleUrl: './reject-invoice-dialog.scss',
})
export class RejectInvoiceDialog {
  private readonly dialogRef = inject<MatDialogRef<RejectInvoiceDialog, string>>(MatDialogRef);

  protected readonly commonReasons = commonRejectionReasons;

  private readonly model = signal({ reason: '' });

  protected readonly rejectForm = form(
    this.model,
    (rejection) => {
      required(rejection.reason, { message: 'Indica el motivo.' });
      // required() accepts "   ", but the domain trims the reason before checking it
      validate(rejection.reason, ({ value }) =>
        value() !== '' && value().trim() === ''
          ? { kind: 'required', message: 'Indica el motivo.' }
          : undefined,
      );
      maxLength(rejection.reason, maxRejectionReasonLength, {
        message: `El motivo no puede superar los ${maxRejectionReasonLength} caracteres.`,
      });
    },
    {
      submission: {
        action: async () => {
          this.dialogRef.close(this.model().reason.trim());
          return undefined;
        },
      },
    },
  );

  protected pick(reason: string): void {
    this.model.set({ reason });
  }
}
