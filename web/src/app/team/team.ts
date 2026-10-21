import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  email,
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
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { CurrentUserService } from '../core/firm/current-user.service';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog/confirm-dialog';
import { Invitation, invitationsUrl, maxEmailLength } from './invitation';
import { InvitationsApi } from './invitations-api';

/** The owner invites people to the firm and manages the invitations that are still open. */
@Component({
  selector: 'app-team',
  imports: [
    DatePipe,
    FormField,
    FormRoot,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  templateUrl: './team.html',
  styleUrl: './team.scss',
})
export class Team {
  private readonly api = inject(InvitationsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly currentUser = inject(CurrentUserService);

  /** Where the invited person has to sign up: this same app. */
  protected readonly appUrl = inject(DOCUMENT).location.origin;

  protected readonly invitations = httpResource<Invitation[]>(() => invitationsUrl);

  private readonly model = signal({ email: '' });

  protected readonly inviteForm = form(
    this.model,
    (invitation) => {
      required(invitation.email, { message: 'El email es obligatorio.' });
      email(invitation.email, { message: 'El email no es válido.' });
      maxLength(invitation.email, maxEmailLength, {
        message: `El email no puede superar los ${maxEmailLength} caracteres.`,
      });
      // The API refuses it too; checking here gives a clearer message
      validate(invitation.email, ({ value }) =>
        value().trim().toLowerCase() === this.currentUser.user()?.email?.toLowerCase()
          ? { kind: 'self', message: 'Ya formas parte de la gestoría.' }
          : undefined,
      );
    },
    { submission: { action: () => this.invite() } },
  );

  private async invite(): Promise<TreeValidationResult> {
    try {
      const invitation = await firstValueFrom(this.api.invite(this.model().email.trim()));
      this.snackBar.open(`Invitación creada para ${invitation.email}.`, 'OK', { duration: 4000 });
      this.inviteForm().reset({ email: '' });
      this.invitations.reload();
      return undefined;
    } catch (error) {
      // The API's messages are in English, so only its status code is used
      return {
        kind: 'server',
        message:
          error instanceof HttpErrorResponse && error.status === 400
            ? 'El email no es válido.'
            : 'No se ha podido crear la invitación. Inténtalo de nuevo.',
      };
    }
  }

  /** Inviting the same address again gives it another week. */
  protected async renew(invitation: Invitation): Promise<void> {
    try {
      await firstValueFrom(this.api.invite(invitation.email));
      this.snackBar.open(`Invitación a ${invitation.email} renovada.`, 'OK', { duration: 4000 });
    } catch {
      this.snackBar.open('No se ha podido renovar la invitación.', 'OK');
    }
    this.invitations.reload();
  }

  protected async revoke(invitation: Invitation): Promise<void> {
    const dialogRef = this.dialog.open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
      data: {
        title: 'Anular invitación',
        message: `${invitation.email} ya no podrá unirse a la gestoría con esta invitación.`,
        confirmText: 'Anular',
      },
    });

    if (!(await firstValueFrom(dialogRef.afterClosed()))) {
      return;
    }

    try {
      await firstValueFrom(this.api.revoke(invitation.id));
      this.snackBar.open(`Invitación a ${invitation.email} anulada.`, 'OK', { duration: 4000 });
    } catch {
      this.snackBar.open('No se ha podido anular la invitación.', 'OK');
    }
    // Also after an error: a 404 means it was accepted or revoked meanwhile, so the list is stale
    this.invitations.reload();
  }
}
