import { Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { Invoice, LedgerAccount } from '../invoice';
import { InvoicesApi } from '../invoices-api';

/**
 * The invoice's ledger account in the list: what Plumy suggests (and why), and a picker to confirm it or choose
 * another. Every choice is saved at once: it is what the next suggestions learn from.
 */
@Component({
  selector: 'app-ledger-account-cell',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule],
  templateUrl: './ledger-account-cell.html',
  styleUrl: './ledger-account-cell.scss',
})
export class LedgerAccountCell {
  private readonly api = inject(InvoicesApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly invoice = input.required<Invoice>();
  /** The chart of accounts. May still be loading (empty). */
  readonly accounts = input.required<readonly LedgerAccount[]>();
  /** The invoice as the API returned it after a change. */
  readonly changed = output<Invoice>();

  protected readonly saving = signal(false);

  /** Approved or rejected: the account is part of that decision, and the API refuses to change it. */
  protected readonly locked = computed(() => this.invoice().reviewStatus !== 'Unreviewed');

  /** A person's choice wins; until then, the suggestion. */
  private readonly current = computed(
    () => this.invoice().ledgerAccount ?? this.invoice().ledgerAccountSuggestion,
  );

  /**
   * What the picker shows. Follows the invoice, but is set at once on a choice; a failed save puts it back, or
   * the picker would show an account that wasn't saved.
   */
  protected readonly selectedCode = linkedSignal(() => this.current()?.code ?? null);

  /** Only while nobody has confirmed an account yet. */
  protected readonly pendingSuggestion = computed(() =>
    this.invoice().ledgerAccount ? null : this.invoice().ledgerAccountSuggestion,
  );

  /** The confirm button: only for a suggestion that can still be confirmed. */
  protected readonly confirmable = computed(() =>
    this.locked() ? null : this.pendingSuggestion(),
  );

  /** The catalog, plus the current account while the catalog loads (or if it left it), so it always shows. */
  protected readonly options = computed(() => {
    const accounts = this.accounts();
    const current = this.current();
    return current && !accounts.some((account) => account.code === current.code)
      ? [{ code: current.code, name: current.name }, ...accounts]
      : accounts;
  });

  protected async confirm(code: string): Promise<void> {
    this.selectedCode.set(code);
    this.saving.set(true);
    try {
      this.changed.emit(
        await firstValueFrom(this.api.confirmLedgerAccount(this.invoice().id, code)),
      );
    } catch {
      this.selectedCode.set(this.current()?.code ?? null);
      this.snackBar.open('No se ha podido guardar la cuenta.', 'OK');
    } finally {
      this.saving.set(false);
    }
  }
}
