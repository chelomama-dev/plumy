import { formatMoney } from './money';

describe('formatMoney', () => {
  it('formats euros the Spanish way', () => {
    expect(formatMoney(1493.82, 'EUR')).toBe('1493,82 €');
    expect(formatMoney(21493.8, 'EUR')).toBe('21.493,80 €');
  });

  it('assumes euros when the currency is unknown', () => {
    expect(formatMoney(121, null)).toBe('121,00 €');
  });

  it('keeps other currencies', () => {
    expect(formatMoney(10, 'USD')).toBe('10,00 US$');
  });

  it('is empty without an amount', () => {
    expect(formatMoney(null, 'EUR')).toBe('');
    expect(formatMoney(undefined)).toBe('');
  });
});
