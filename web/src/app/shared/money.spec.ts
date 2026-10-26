import { formatAmountInput, formatMoney, parseAmountInput } from './money';

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

describe('formatAmountInput', () => {
  it('writes two decimals with a comma and no thousands separator', () => {
    expect(formatAmountInput(1493.8)).toBe('1493,80');
    expect(formatAmountInput(-121)).toBe('-121,00');
  });

  it('is empty without an amount', () => {
    expect(formatAmountInput(null)).toBe('');
  });
});

describe('parseAmountInput', () => {
  it.each([
    ['1.234,56', 1234.56],
    ['1234,5', 1234.5],
    ['1234.56', 1234.56],
    ['-121', -121],
    [' 121,00 € ', 121],
  ])('reads %s', (text, expected) => {
    expect(parseAmountInput(text)).toBe(expected);
  });

  it('reads empty as no amount', () => {
    expect(parseAmountInput('  ')).toBeNull();
  });

  it.each(['12,345', 'doce', '1,2,3', '12e3'])('rejects %s', (text) => {
    expect(parseAmountInput(text)).toBeUndefined();
  });
});
