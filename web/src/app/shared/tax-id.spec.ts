import { isValidTaxId, normalizeTaxId } from './tax-id';

// Same cases as tests/Plumy.Domain.Tests/Common/TaxIdTests.cs: the API is the source of truth,
// this copy only gives instant feedback in the form.
describe('tax-id', () => {
  it.each([
    '12345678Z',
    '00000000T',
    '87654321X', // NIF
    'X1234567L',
    'Y1234567X',
    'Z1234567R', // NIE
    'B12345674',
    'B87654323',
    'A00000000',
    'Q1234567D',
    'G12345674',
    'G1234567D', // CIF
  ])('accepts %s', (value) => {
    expect(isValidTaxId(value)).toBe(true);
  });

  it.each([
    ['12345678z', '12345678Z'],
    [' 12345678Z ', '12345678Z'],
    ['12.345.678-Z', '12345678Z'],
    ['b-1234567-4', 'B12345674'],
    ['x 1234567 l', 'X1234567L'],
  ])('normalizes %s to %s', (value, expected) => {
    expect(normalizeTaxId(value)).toBe(expected);
    expect(isValidTaxId(value)).toBe(true);
  });

  it.each([
    '',
    '   ',
    'patata',
    '12345678', // missing letter
    '1234567Z', // too short
    '123456789Z', // too long
    '12345678A', // wrong NIF letter
    'X1234567A', // wrong NIE letter
    'B12345675', // wrong CIF digit
    'B1234567D', // B requires a digit, not a letter
    'Q12345674', // Q requires a letter, not a digit
    'I12345674', // I is not a valid CIF letter
  ])('rejects "%s"', (value) => {
    expect(isValidTaxId(value)).toBe(false);
  });
});
