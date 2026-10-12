// Port of Plumy.Domain.Common.TaxId (NIF, NIE and CIF). Keep both in sync: same cases in tax-id.spec.ts.
// The API validates again; this copy only exists so the form can say "invalid" without a round trip.

const nifLetters = 'TRWAGMYFPDXBNJZSQVHLCKE';
const niePrefixes = 'XYZ';
const cifControlLetters = 'JABCDEFGHI';

const nifPattern = /^\d{8}[A-Z]$/;
const niePattern = /^[XYZ]\d{7}[A-Z]$/;
const cifPattern = /^[ABCDEFGHJNPQRSUVW]\d{7}[0-9A-J]$/;

/** Removes spaces, dots and dashes and upper-cases: " b-1234567.4 " → "B12345674". */
export function normalizeTaxId(value: string): string {
  return value.replace(/[\s.-]/g, '').toUpperCase();
}

export function isValidTaxId(value: string): boolean {
  const taxId = normalizeTaxId(value);

  return (
    (nifPattern.test(taxId) && hasValidNifLetter(taxId)) ||
    (niePattern.test(taxId) && hasValidNieLetter(taxId)) ||
    (cifPattern.test(taxId) && hasValidCifControl(taxId))
  );
}

function hasValidNifLetter(nif: string): boolean {
  return nif[8] === nifLetters[Number(nif.slice(0, 8)) % 23];
}

// A NIE is a NIF where the leading X/Y/Z stands for 0/1/2
function hasValidNieLetter(nie: string): boolean {
  return hasValidNifLetter(niePrefixes.indexOf(nie[0]) + nie.slice(1));
}

function hasValidCifControl(cif: string): boolean {
  let sum = 0;
  for (let i = 1; i <= 7; i++) {
    let digit = Number(cif[i]);

    // Odd positions (1, 3, 5, 7) are doubled and their digits added: 7 → 14 → 1 + 4 = 5
    if (i % 2 === 1) {
      digit *= 2;
      digit = Math.floor(digit / 10) + (digit % 10);
    }

    sum += digit;
  }

  const control = (10 - (sum % 10)) % 10;
  const isDigitMatch = cif[8] === String(control);
  const isLetterMatch = cif[8] === cifControlLetters[control];

  switch (cif[0]) {
    case 'A':
    case 'B':
    case 'E':
    case 'H':
      return isDigitMatch;
    case 'N':
    case 'P':
    case 'Q':
    case 'R':
    case 'S':
    case 'W':
      return isLetterMatch;
    default:
      return isDigitMatch || isLetterMatch;
  }
}
