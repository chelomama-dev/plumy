import { companyInitials } from './client-company-picker';

describe('companyInitials', () => {
  it('takes the first letters of the first two words', () => {
    expect(companyInitials('Talleres Martínez S.L.')).toBe('TM');
    expect(companyInitials('Clínica Dental Ruiz')).toBe('CD');
  });

  it('leaves out the legal form', () => {
    expect(companyInitials('Acme S.L.')).toBe('A');
    expect(companyInitials('Ópticas SA')).toBe('Ó');
  });

  it('skips words that do not start with a letter', () => {
    expect(companyInitials('3D Print Lab')).toBe('PL');
  });
});
