import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { RejectInvoiceDialog } from './reject-invoice-dialog';

describe('RejectInvoiceDialog', () => {
  let fixture: ComponentFixture<RejectInvoiceDialog>;
  let close: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [RejectInvoiceDialog],
      providers: [{ provide: MatDialogRef, useValue: { close } }],
    });
    fixture = TestBed.createComponent(RejectInvoiceDialog);
    await fixture.whenStable();
  });

  const element = () => fixture.nativeElement as HTMLElement;
  const textarea = () => element().querySelector('textarea')!;
  const submit = async () => {
    element().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  };

  it('closes with the reason typed, trimmed', async () => {
    textarea().value = '  Factura proforma  ';
    textarea().dispatchEvent(new Event('input'));

    await submit();

    expect(close).toHaveBeenCalledWith('Factura proforma');
  });

  it('fills in a common reason with one click', async () => {
    Array.from(element().querySelectorAll<HTMLElement>('mat-chip'))
      .find((chip) => chip.textContent?.includes('Duplicada'))!
      .click();
    await fixture.whenStable();

    expect(textarea().value).toBe('Duplicada');
    await submit();
    expect(close).toHaveBeenCalledWith('Duplicada');
  });

  it('needs a reason', async () => {
    textarea().value = '   ';
    textarea().dispatchEvent(new Event('input'));

    await submit();

    expect(close).not.toHaveBeenCalled();
    expect(element().textContent).toContain('Indica el motivo.');
  });
});
