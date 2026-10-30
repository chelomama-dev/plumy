import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CurrentUserService } from '../../core/firm/current-user.service';
import { ConfidenceBandAccuracy } from '../ai-accuracy';
import { autoApprovalUrl, correctedAbove } from '../auto-approval';
import { AutoApprovalCard } from './auto-approval-card';

const bands: ConfidenceBandAccuracy[] = [
  { from: 0, to: 0.5, read: 2, corrected: 2 },
  { from: 0.5, to: 0.8, read: 4, corrected: 3 },
  { from: 0.8, to: 0.9, read: 10, corrected: 4 },
  { from: 0.9, to: 0.95, read: 10, corrected: 1 },
  { from: 0.95, to: 1, read: 40, corrected: 1 },
];

describe('correctedAbove', () => {
  it('adds up the bands from the line up', () => {
    expect(correctedAbove(bands, 0.95)).toEqual({ read: 40, corrected: 1 });
    expect(correctedAbove(bands, 0.9)).toEqual({ read: 50, corrected: 2 });
    expect(correctedAbove(bands, 0.8)).toEqual({ read: 60, corrected: 6 });
  });

  it('has nothing to say without fields that sure', () => {
    expect(correctedAbove([], 0.95)).toBeNull();
  });
});

describe('AutoApprovalCard', () => {
  let fixture: ComponentFixture<AutoApprovalCard>;
  let http: HttpTestingController;
  const role = signal<'Owner' | 'Member'>('Owner');
  const snackBar = { open: vi.fn() };

  beforeEach(() => {
    role.set('Owner');
    snackBar.open.mockClear();
    TestBed.configureTestingModule({
      imports: [AutoApprovalCard],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: CurrentUserService,
          useValue: { firm: () => ({ id: 'f', name: 'G', role: role() }) },
        },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(enabled = false, minConfidence = 0.95) {
    fixture = TestBed.createComponent(AutoApprovalCard);
    fixture.componentRef.setInput('bands', bands);
    TestBed.tick();
    http.expectOne(autoApprovalUrl).flush({ enabled, minConfidence });
    await fixture.whenStable();
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent?.replace(/\s+/g, ' ') ?? '';
  const toggle = () => element().querySelector<HTMLButtonElement>('mat-slide-toggle button')!;
  const option = (label: string) =>
    [...element().querySelectorAll<HTMLButtonElement>('mat-button-toggle button')].find((b) =>
      b.textContent?.includes(label),
    )!;

  it('shows how often the fields above the line had to be corrected', async () => {
    await render(false, 0.9);

    expect(text()).toContain('Desactivada');
    expect(text()).toContain('con 90 % o más se corrigieron 2 de 50 (4 %)');
  });

  it('lets the owner turn it on, saving it straight away', async () => {
    await render();

    toggle().click();
    TestBed.tick();
    const request = http.expectOne(autoApprovalUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ enabled: true, minConfidence: 0.95 });
    request.flush({ enabled: true, minConfidence: 0.95 });
    await fixture.whenStable();

    expect(text()).toContain('Activada');
    expect(snackBar.open).toHaveBeenCalledWith('Aprobación automática activada.', 'OK', {
      duration: 4000,
    });
  });

  it('lets the owner move the line', async () => {
    await render(true, 0.95);

    option('80 %').click();
    TestBed.tick();
    const request = http.expectOne(autoApprovalUrl);
    expect(request.request.body).toEqual({ enabled: true, minConfidence: 0.8 });
    request.flush({ enabled: true, minConfidence: 0.8 });
    await fixture.whenStable();

    expect(text()).toContain('con 80 % o más se corrigieron 6 de 60');
  });

  it('puts the setting back when saving fails', async () => {
    await render();

    toggle().click();
    TestBed.tick();
    http.expectOne(autoApprovalUrl).flush(null, { status: 500, statusText: 'Error' });
    // The failed save settles a task later than a successful one
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();

    expect(text()).toContain('Desactivada');
    expect(snackBar.open).toHaveBeenCalledWith('No se ha podido guardar el cambio.', 'OK');
  });

  it('only shows it to a member', async () => {
    role.set('Member');
    await render(true);

    expect(toggle().disabled).toBe(true);
    expect(option('90 %').disabled).toBe(true);
    expect(text()).toContain('Solo el titular de la gestoría puede cambiarlo.');
  });
});
