import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { CurrentUserService } from '../../core/firm/current-user.service';
import { ConfidenceBandAccuracy } from '../ai-accuracy';
import {
  AutoApproval,
  autoApprovalUrl,
  correctedAbove,
  minConfidenceOptions,
} from '../auto-approval';

/**
 * Whether Plumy approves by itself the invoices it is sure about, next to the numbers that say how sure is sure.
 * Everyone sees it; only the owner changes it, and each change is saved straight away.
 */
@Component({
  selector: 'app-auto-approval-card',
  imports: [MatButtonToggleModule, MatIconModule, MatSlideToggleModule],
  templateUrl: './auto-approval-card.html',
  styleUrl: './auto-approval-card.scss',
})
export class AutoApprovalCard {
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly currentUser = inject(CurrentUserService);

  /** The report's confidence bands, for the evidence line. Empty while there is no report. */
  readonly bands = input<readonly ConfidenceBandAccuracy[]>([]);

  protected readonly options = minConfidenceOptions;
  protected readonly isOwner = computed(() => this.currentUser.firm()?.role === 'Owner');

  protected readonly stored = httpResource<AutoApproval>(() => autoApprovalUrl);

  /** What the screen shows: the stored setting, or the change being saved. */
  protected readonly setting = linkedSignal(() =>
    this.stored.hasValue() ? this.stored.value() : undefined,
  );
  protected readonly saving = signal(false);

  protected readonly evidence = computed(() => {
    const setting = this.setting();
    return setting ? correctedAbove(this.bands(), setting.minConfidence) : null;
  });

  protected percent(value: number): string {
    return `${Math.round(value * 100)} %`;
  }

  protected async save(change: Partial<AutoApproval>): Promise<void> {
    const before = this.setting();
    if (!before) {
      return;
    }
    const after = { ...before, ...change };
    this.setting.set(after);
    this.saving.set(true);
    try {
      this.setting.set(await firstValueFrom(this.http.put<AutoApproval>(autoApprovalUrl, after)));
      this.snackBar.open(
        after.enabled ? 'Aprobación automática activada.' : 'Aprobación automática desactivada.',
        'OK',
        { duration: 4000 },
      );
    } catch {
      this.setting.set(before);
      this.snackBar.open('No se ha podido guardar el cambio.', 'OK');
    } finally {
      this.saving.set(false);
    }
  }
}
