import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  form,
  FormField,
  FormRoot,
  maxLength,
  required,
  TreeValidationResult,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { CurrentUserService } from '../core/firm/current-user.service';
import { maxFirmNameLength } from '../core/firm/current-user';

/** First step after signing up: the user creates their accounting firm and becomes its owner. */
@Component({
  selector: 'app-create-firm',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './create-firm.html',
  styleUrl: './create-firm.scss',
})
export class CreateFirm {
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);

  protected readonly userName = () => this.currentUser.user()?.name;

  private readonly model = signal({ name: '' });

  protected readonly firmForm = form(
    this.model,
    (firm) => {
      required(firm.name, { message: 'El nombre es obligatorio.' });
      // required() accepts "   ", but the domain trims the name before checking it
      validate(firm.name, ({ value }) =>
        value() !== '' && value().trim() === ''
          ? { kind: 'required', message: 'El nombre es obligatorio.' }
          : undefined,
      );
      maxLength(firm.name, maxFirmNameLength, {
        message: `El nombre no puede superar los ${maxFirmNameLength} caracteres.`,
      });
    },
    { submission: { action: () => this.save() } },
  );

  private async save(): Promise<TreeValidationResult> {
    try {
      await this.currentUser.createFirm(this.model().name);
      await this.router.navigateByUrl('/');
      return undefined;
    } catch (error) {
      return this.toFormErrors(error);
    }
  }

  private async toFormErrors(error: unknown): Promise<TreeValidationResult> {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 409:
          // Created from another tab, or joined meanwhile: what we knew about the user is stale
          this.currentUser.reset();
          await this.router.navigateByUrl('/');
          return undefined;
        case 400:
          return { kind: 'server', message: error.error?.detail ?? 'Los datos no son válidos.' };
      }
    }

    return { kind: 'server', message: 'No se ha podido crear la gestoría. Inténtalo de nuevo.' };
  }
}
