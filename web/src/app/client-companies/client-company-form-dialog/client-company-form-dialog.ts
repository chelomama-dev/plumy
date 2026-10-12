import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  disabled,
  form,
  FormField,
  FormRoot,
  maxLength,
  required,
  TreeValidationResult,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { isValidTaxId } from '../../shared/tax-id';
import { ClientCompaniesApi } from '../client-companies-api';
import { ClientCompany, maxClientCompanyNameLength } from '../client-company';

export interface ClientCompanyFormDialogData {
  /** Present when renaming; absent when creating. */
  company?: ClientCompany;
}

interface ClientCompanyFormModel {
  name: string;
  taxId: string;
}

/** Create or rename a client company. Closes with the saved company; undefined when cancelled. */
@Component({
  selector: 'app-client-company-form-dialog',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './client-company-form-dialog.html',
  styleUrl: './client-company-form-dialog.scss',
})
export class ClientCompanyFormDialog {
  private readonly api = inject(ClientCompaniesApi);
  private readonly dialogRef =
    inject<MatDialogRef<ClientCompanyFormDialog, ClientCompany>>(MatDialogRef);
  private readonly company = inject<ClientCompanyFormDialogData>(MAT_DIALOG_DATA).company;

  protected readonly isRename = this.company !== undefined;

  /** The form does not copy the data: it reads and writes this signal. */
  private readonly model = signal<ClientCompanyFormModel>({
    name: this.company?.name ?? '',
    taxId: this.company?.taxId ?? '',
  });

  protected readonly companyForm = form(
    this.model,
    (company) => {
      required(company.name, { message: 'El nombre es obligatorio.' });
      // required() accepts "   ", but the domain trims the name before checking it
      validate(company.name, ({ value }) =>
        value() !== '' && value().trim() === ''
          ? { kind: 'required', message: 'El nombre es obligatorio.' }
          : undefined,
      );
      maxLength(company.name, maxClientCompanyNameLength, {
        message: `El nombre no puede superar los ${maxClientCompanyNameLength} caracteres.`,
      });

      // The tax ID identifies the company: it can only be set when creating it
      disabled(company.taxId, () => this.isRename);
      required(company.taxId, { message: 'El NIF es obligatorio.' });
      validate(company.taxId, ({ value }) =>
        value() === '' || isValidTaxId(value())
          ? undefined
          : { kind: 'taxId', message: 'No es un NIF, NIE o CIF válido.' },
      );
    },
    // Run by <form [formRoot]> on submit, only when the form is valid
    { submission: { action: () => this.save() } },
  );

  private async save(): Promise<TreeValidationResult> {
    const { name, taxId } = this.model();

    try {
      const saved = await firstValueFrom(
        this.company ? this.api.rename(this.company.id, name) : this.api.create({ name, taxId }),
      );
      this.dialogRef.close(saved);
      return undefined;
    } catch (error) {
      // Returned errors are attached to the form like any validation error
      return this.toFormErrors(error);
    }
  }

  private toFormErrors(error: unknown): TreeValidationResult {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 409:
          return {
            kind: 'conflict',
            fieldTree: this.companyForm.taxId,
            message: 'Ya existe una empresa cliente con este NIF (puede que esté archivada).',
          };
        case 404:
          return { kind: 'notFound', message: 'Esta empresa ya no existe o se ha archivado.' };
        case 400:
          // The form validates the same rules, so this means the two have drifted apart
          return {
            kind: 'server',
            message: error.error?.detail ?? 'Los datos no son válidos.',
          };
      }
    }

    return { kind: 'server', message: 'No se ha podido guardar. Inténtalo de nuevo.' };
  }
}
