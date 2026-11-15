import { Injectable, Provider, inject } from '@angular/core';
import {
  DateAdapter,
  MAT_DATE_FORMATS,
  MAT_DATE_LOCALE,
  MAT_NATIVE_DATE_FORMATS,
  MatDateFormats,
  NativeDateAdapter,
} from '@angular/material/core';

const isoPattern = /^(\d{4})-(\d{2})-(\d{2})$/;
// What people type in Spain: 15/11/2026, 5-1-26, 15.11.2026
const spanishPattern = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2}|\d{4})$/;
const invalidDate = 'invalid';

const pad = (n: number, width = 2) => String(n).padStart(width, '0');

/** A local date (no time zone shift) as yyyy-MM-dd. */
export function toIsoDate(date: Date): string {
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function realDate(year: number, month: number, day: number): string | null {
  const date = new Date(year, month - 1, day);
  // new Date rolls 31/02 over to March: that's a typo, not a date
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
    ? toIsoDate(date)
    : null;
}

/**
 * The datepicker's dates as the yyyy-MM-dd strings the API and the forms already use, so a date field binds to a
 * string like any other input. The calendar maths are the native adapter's, on a local Date.
 */
@Injectable()
export class IsoDateAdapter extends DateAdapter<string> {
  private readonly native = new NativeDateAdapter();

  constructor() {
    super();
    super.setLocale(inject(MAT_DATE_LOCALE, { optional: true }) ?? 'es-ES');
  }

  override setLocale(locale: unknown): void {
    super.setLocale(locale);
    this.native.setLocale(locale);
  }

  private toDate(value: string): Date {
    const match = isoPattern.exec(value);
    return match ? new Date(+match[1], +match[2] - 1, +match[3]) : new Date(NaN);
  }

  getYear = (date: string) => this.native.getYear(this.toDate(date));
  getMonth = (date: string) => this.native.getMonth(this.toDate(date));
  getDate = (date: string) => this.native.getDate(this.toDate(date));
  getDayOfWeek = (date: string) => this.native.getDayOfWeek(this.toDate(date));
  getMonthNames = (style: 'long' | 'short' | 'narrow') => this.native.getMonthNames(style);
  getDateNames = () => this.native.getDateNames();
  getDayOfWeekNames = (style: 'long' | 'short' | 'narrow') => this.native.getDayOfWeekNames(style);
  getYearName = (date: string) => this.native.getYearName(this.toDate(date));
  // Weeks start on Monday in Spain, whatever the browser says
  getFirstDayOfWeek = () => 1;
  getNumDaysInMonth = (date: string) => this.native.getNumDaysInMonth(this.toDate(date));
  clone = (date: string) => date;
  createDate = (year: number, month: number, date: number) =>
    toIsoDate(this.native.createDate(year, month, date));
  today = () => toIsoDate(new Date());

  parse(value: unknown): string | null {
    if (typeof value !== 'string' || value.trim() === '') {
      return null;
    }
    const text = value.trim();
    const iso = isoPattern.exec(text);
    if (iso) {
      return realDate(+iso[1], +iso[2], +iso[3]) ?? invalidDate;
    }
    const spanish = spanishPattern.exec(text);
    if (spanish) {
      const year = spanish[3].length === 2 ? 2000 + +spanish[3] : +spanish[3];
      return realDate(year, +spanish[2], +spanish[1]) ?? invalidDate;
    }
    return invalidDate;
  }

  format(date: string, displayFormat: object): string {
    if (!this.isValid(date)) {
      throw Error('IsoDateAdapter: Cannot format invalid date.');
    }
    return this.native.format(this.toDate(date), displayFormat);
  }

  addCalendarYears = (date: string, years: number) =>
    toIsoDate(this.native.addCalendarYears(this.toDate(date), years));
  addCalendarMonths = (date: string, months: number) =>
    toIsoDate(this.native.addCalendarMonths(this.toDate(date), months));
  addCalendarDays = (date: string, days: number) =>
    toIsoDate(this.native.addCalendarDays(this.toDate(date), days));
  toIso8601 = (date: string) => date;
  isDateInstance = (value: unknown) => typeof value === 'string';
  isValid = (date: string) => isoPattern.test(date) && !isNaN(this.toDate(date).getTime());
  invalid = () => invalidDate;

  /** An empty field is no date, not an invalid one. */
  override deserialize(value: unknown): string | null {
    if (value === '' || value == null) {
      return null;
    }
    return typeof value === 'string' ? this.parse(value) : super.deserialize(value);
  }
}

/** Dates in the field as 05/01/2026, like everywhere else in the app (the native default would be 5/1/2026). */
const plumyDateFormats: MatDateFormats = {
  ...MAT_NATIVE_DATE_FORMATS,
  display: {
    ...MAT_NATIVE_DATE_FORMATS.display,
    dateInput: { day: '2-digit', month: '2-digit', year: 'numeric' },
  },
};

/**
 * The datepicker in Spanish, on yyyy-MM-dd strings. Provide it in the components that have a date field, not in
 * app.config, so the datepicker stays out of the initial bundle.
 */
export function provideIsoDateAdapter(): Provider[] {
  return [
    { provide: MAT_DATE_LOCALE, useValue: 'es-ES' },
    { provide: DateAdapter, useClass: IsoDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: plumyDateFormats },
  ];
}
