import { fileNameFromContentDisposition } from './save-file';

describe('fileNameFromContentDisposition', () => {
  it('prefers the UTF-8 name', () => {
    expect(
      fileNameFromContentDisposition(
        "attachment; filename=facturas.csv; filename*=UTF-8''facturas-%C3%B1.csv",
      ),
    ).toBe('facturas-ñ.csv');
  });

  it('takes the plain name, quoted or not', () => {
    expect(fileNameFromContentDisposition('attachment; filename="facturas todas.xlsx"')).toBe(
      'facturas todas.xlsx',
    );
    expect(fileNameFromContentDisposition('attachment; filename=facturas.xlsx')).toBe(
      'facturas.xlsx',
    );
  });

  it('is null without a name', () => {
    expect(fileNameFromContentDisposition(null)).toBeNull();
    expect(fileNameFromContentDisposition('attachment')).toBeNull();
  });
});
