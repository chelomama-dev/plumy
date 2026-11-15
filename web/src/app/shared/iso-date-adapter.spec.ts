import { TestBed } from '@angular/core/testing';
import { DateAdapter } from '@angular/material/core';
import { IsoDateAdapter, provideIsoDateAdapter, toIsoDate } from './iso-date-adapter';

describe('IsoDateAdapter', () => {
  let adapter: IsoDateAdapter;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideIsoDateAdapter()] });
    adapter = TestBed.inject(DateAdapter) as IsoDateAdapter;
  });

  it('reads what people type in Spain, and ISO dates', () => {
    expect(adapter.parse('15/11/2026')).toBe('2026-11-15');
    expect(adapter.parse('5-1-26')).toBe('2026-01-05');
    expect(adapter.parse('05.01.2026')).toBe('2026-01-05');
    expect(adapter.parse('2026-01-05')).toBe('2026-01-05');
  });

  it('tells an empty field apart from a date it cannot read', () => {
    expect(adapter.parse('')).toBeNull();
    expect(adapter.parse('  ')).toBeNull();
    expect(adapter.isValid(adapter.parse('31/02/2026')!)).toBe(false);
    expect(adapter.isValid(adapter.parse('mañana')!)).toBe(false);
  });

  it('shows dates as dd/mm/yyyy, weeks from Monday', () => {
    expect(
      adapter.format('2026-01-05', { day: '2-digit', month: '2-digit', year: 'numeric' }),
    ).toBe('05/01/2026');
    expect(adapter.getFirstDayOfWeek()).toBe(1);
  });

  it('does calendar maths on yyyy-MM-dd strings', () => {
    expect(adapter.addCalendarMonths('2026-01-31', 1)).toBe('2026-02-28');
    expect(adapter.addCalendarDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(adapter.createDate(2026, 0, 5)).toBe('2026-01-05');
    expect(adapter.getDayOfWeek('2026-11-15')).toBe(0);
  });

  it('turns an empty value into no date', () => {
    expect(adapter.deserialize('')).toBeNull();
    expect(adapter.deserialize('2026-01-05')).toBe('2026-01-05');
  });

  it('writes local dates without a time zone shift', () => {
    expect(toIsoDate(new Date(2026, 0, 1, 23, 59))).toBe('2026-01-01');
  });
});
