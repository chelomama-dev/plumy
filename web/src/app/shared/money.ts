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
