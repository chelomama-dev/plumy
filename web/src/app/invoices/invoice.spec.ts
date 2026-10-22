import { checkInvoiceFile, maxInvoiceFileSizeBytes } from './invoice';

describe('checkInvoiceFile', () => {
  const file = (name: string, size = 1000) => {
    const f = new File(['x'], name);
    Object.defineProperty(f, 'size', { value: size });
    return f;
  };

  it('accepts PDF, JPG and PNG whatever the case of the extension', () => {
    for (const name of ['factura.pdf', 'FOTO.JPG', 'ticket.jpeg', 'escaneo.Png']) {
      expect(checkInvoiceFile(file(name))).toBeNull();
    }
  });

  it('rejects other extensions', () => {
    expect(checkInvoiceFile(file('factura.docx'))).toBe('Solo se aceptan PDF, JPG y PNG.');
    expect(checkInvoiceFile(file('pdf'))).toBe('Solo se aceptan PDF, JPG y PNG.');
  });

  it('rejects empty files and files over 10 MB', () => {
    expect(checkInvoiceFile(file('factura.pdf', 0))).toBe('El archivo está vacío.');
    expect(checkInvoiceFile(file('factura.pdf', maxInvoiceFileSizeBytes))).toBeNull();
    expect(checkInvoiceFile(file('factura.pdf', maxInvoiceFileSizeBytes + 1))).toBe(
      'Supera el máximo de 10 MB.',
    );
  });
});
