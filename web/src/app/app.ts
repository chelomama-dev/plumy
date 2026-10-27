import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/firm/current-user.service';
import { InvoiceActivity, InvoicePhase, invoicePhase } from './invoices/invoice-activity';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly firm = inject(CurrentUserService).firm;

  private readonly activity = inject(InvoiceActivity);

  /** While the AI works on this person's invoices: how many. Null (hidden) otherwise. */
  protected readonly activityLabel = computed(() => {
    const count = this.activity.active().length;
    if (count === 0) {
      return null;
    }
    return count === 1 ? 'La IA está con 1 factura' : `La IA está con ${count} facturas`;
  });

  /** One line per invoice: which and at what step. */
  protected readonly activityDetail = computed(() =>
    this.activity
      .active()
      .map((invoice) => `${invoice.fileName}: ${phaseLabels[invoicePhase(invoice)]}`)
      .join('\n'),
  );
}

const phaseLabels: Record<InvoicePhase, string> = {
  queued: 'en cola',
  reading: 'leyendo el documento',
  suggesting: 'eligiendo la cuenta',
};
