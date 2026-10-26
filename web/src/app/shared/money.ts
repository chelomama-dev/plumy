/** "1.493,82 €", "1.200,00 US$". Without a currency, euros: Spanish invoices almost always are. */
export function formatMoney(amount: number | null | undefined, currency?: string | null): string {
  if (amount === null || amount === undefined) {
    return '';
  }
  return new Intl.NumberFormat('es-ES', {
    style: 'currency',
    currency: currency ?? 'EUR',
  }).format(amount);
}

/** An amount as a person edits it: "1234,56" (decimal comma, no thousands separator). Empty for none. */
export function formatAmountInput(amount: number | null | undefined): string {
  return amount === null || amount === undefined ? '' : amount.toFixed(2).replace('.', ',');
}

/**
 * What a person typed as an amount: "1.234,56", "1234,5", "1234.56" or "-121". Empty is null (no amount);
 * anything else that isn't an amount with at most two decimals is undefined.
 */
export function parseAmountInput(text: string): number | null | undefined {
  let value = text.replace(/[\s€]/g, '');
  if (value === '') {
    return null;
  }
  // With a comma, it's the decimal one and dots group thousands; without, a lone dot is the decimal point
  if (value.includes(',')) {
    value = value.replace(/\./g, '').replace(',', '.');
  }
  return /^-?\d+(\.\d{1,2})?$/.test(value) ? Number(value) : undefined;
}
