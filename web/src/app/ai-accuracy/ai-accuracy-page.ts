import { httpResource } from '@angular/common/http';
import { Component, computed, inject, InjectionToken, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ClientCompany } from '../client-companies/client-company';
import { ClientCompanyPicker } from '../client-companies/client-company-picker/client-company-picker';
import {
  AiAccuracyReport,
  aiAccuracyUrl,
  ConfidenceBandAccuracy,
  fieldLabels,
  isDoubtfulBand,
  Period,
  periodLabels,
  periodStart,
  rate,
} from './ai-accuracy';

/** Today, so a test can fix the periods. */
export const TODAY = new InjectionToken<() => Date>('TODAY', { factory: () => () => new Date() });

/** "87 %", or a dash when there is nothing to count. */
export function formatRate(value: number | null): string {
  return value === null ? '—' : `${Math.round(value * 100)} %`;
}

function formatBand(band: ConfidenceBandAccuracy): string {
  const percent = (value: number) => Math.round(value * 100);
  return `${percent(band.from)}–${percent(band.to)} %`;
}

/**
 * How well the AI is doing: how often a person kept the account it suggested, and which fields of the invoices it
 * reads wrong. Over the approved invoices only: a person checked those, so what they kept is the right answer.
 */
@Component({
  selector: 'app-ai-accuracy-page',
  imports: [
    ClientCompanyPicker,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './ai-accuracy-page.html',
  styleUrl: './ai-accuracy-page.scss',
})
export class AiAccuracyPage {
  private readonly today = inject(TODAY);

  protected readonly periodLabels = periodLabels;
  protected readonly company = signal<ClientCompany | undefined>(undefined);
  protected readonly period = signal<Period>(90);

  protected readonly report = httpResource<AiAccuracyReport>(() => {
    const company = this.company();
    const days = this.period();
    return {
      url: aiAccuracyUrl,
      params: {
        ...(company ? { clientCompanyId: company.id } : {}),
        ...(days ? { from: periodStart(days, this.today()) } : {}),
      },
    };
  });

  /** The last report, kept on screen while the next one loads. */
  protected readonly value = computed(() =>
    this.report.hasValue() ? this.report.value() : undefined,
  );

  protected readonly aiRate = computed(() => {
    const ai = this.value()?.ledgerAccounts.ai;
    return ai ? rate(ai.accepted, ai.suggested) : null;
  });

  protected readonly historyRate = computed(() => {
    const history = this.value()?.ledgerAccounts.supplierHistory;
    return history ? rate(history.accepted, history.suggested) : null;
  });

  /** Fields read right (not corrected) over every field the reader returned. */
  protected readonly fieldsRate = computed(() => {
    const fields = this.value()?.fields ?? [];
    const read = fields.reduce((sum, f) => sum + f.read, 0);
    const corrected = fields.reduce((sum, f) => sum + f.corrected, 0);
    return rate(read - corrected, read);
  });

  /** Worst first: what to look at. Fields never read nor filled in say nothing and are left out. */
  protected readonly fields = computed(() =>
    (this.value()?.fields ?? [])
      .filter((f) => f.read + f.missed > 0)
      .map((f) => ({
        label: fieldLabels[f.field],
        read: f.read,
        corrected: f.corrected,
        missed: f.missed,
        rightRate: rate(f.read - f.corrected, f.read),
      }))
      .sort((a, b) => (a.rightRate ?? 1) - (b.rightRate ?? 1)),
  );

  /** High confidence first, as people read it: "when the reader is sure, is it right?" */
  protected readonly bands = computed(() =>
    [...(this.value()?.confidenceBands ?? [])].reverse().map((band) => ({
      label: formatBand(band),
      doubtful: isDoubtfulBand(band),
      read: band.read,
      corrected: band.corrected,
      rightRate: rate(band.read - band.corrected, band.read),
    })),
  );

  protected readonly formatRate = formatRate;

  /** The meter's width: nothing to count draws no bar. */
  protected width(value: number | null): string {
    return `${Math.round((value ?? 0) * 100)}%`;
  }
}
