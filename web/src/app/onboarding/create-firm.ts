import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
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
import { maxFirmNameLength, PendingInvitation } from '../core/firm/current-user';

/**
 * First step after signing up: the user joins a firm that invited them, or creates their own and becomes its owner.
 */
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

  protected readonly invitations = computed(() => this.currentUser.user()?.invitations ?? []);

  /** Id of the invitation being accepted, to disable the buttons meanwhile. */
  protected readonly accepting = signal<string | null>(null);
  protected readonly acceptError = signal<string | null>(null);

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

  protected async accept(invitation: PendingInvitation): Promise<void> {
    this.accepting.set(invitation.id);
    this.acceptError.set(null);
    try {
      await this.currentUser.acceptInvitation(invitation.id);
      await this.router.navigateByUrl('/');
    } catch (error) {
      await this.onAcceptFailed(error instanceof HttpErrorResponse ? error.status : 0);
    } finally {
      this.accepting.set(null);
    }
  }

  private async onAcceptFailed(status: number): Promise<void> {
    if (status === 409) {
      // Joined or created a firm from another tab meanwhile
      this.currentUser.reset();
      await this.router.navigateByUrl('/');
      return;
    }

    // The API's messages are in English, so only its status code is used
    const messages: Record<number, string> = {
      400: 'La invitación ha caducado. Pide a la gestoría que te invite de nuevo.',
      404: 'La invitación ya no está disponible: puede que la gestoría la haya anulado.',
    };
    this.acceptError.set(
      messages[status] ?? 'No se ha podido aceptar la invitación. Inténtalo de nuevo.',
    );

    if (status in messages) {
      // The list on screen is stale: ask the API again for the invitations that are still valid
      this.currentUser.reset();
      await this.currentUser.load().catch(() => undefined);
    }
  }

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
