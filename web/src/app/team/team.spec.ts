import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { currentUserUrl } from '../core/firm/current-user';
import { CurrentUserService } from '../core/firm/current-user.service';
import { FirmMembership } from '../core/firm/current-user';
import { aFirm, aUser } from '../core/firm/test-data';
import { ConfirmDialog } from '../shared/confirm-dialog/confirm-dialog';
import { Invitation, invitationsUrl } from './invitation';
import { Member, membersUrl } from './member';
import { Team } from './team';

describe('Team', () => {
  let fixture: ComponentFixture<Team>;
  let http: HttpTestingController;
  /** What the confirmation dialog closes with. ConfirmDialog is trivial; here only the flow matters. */
  let dialogResult: boolean | undefined;
  let openDialog: ReturnType<typeof vi.fn>;

  const owner: Member = {
    id: 'owner-id',
    name: 'Ana García',
    email: 'ana@gestoria.es',
    role: 'Owner',
    isCurrentUser: true,
  };
  const member: Member = {
    id: 'member-id',
    name: 'Luis Pérez',
    email: 'luis@gestoria.es',
    role: 'Member',
    isCurrentUser: false,
  };

  const invitation = (n: number, isExpired = false): Invitation => ({
    id: `id-${n}`,
    email: `persona${n}@example.com`,
    createdAt: '2026-10-21T09:00:00+00:00',
    expiresAt: '2026-10-28T09:00:00+00:00',
    isExpired,
  });

  beforeEach(async () => {
    dialogResult = undefined;
    openDialog = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));
    TestBed.configureTestingModule({
      imports: [Team],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: openDialog } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  /** Opens the page as a user of the given firm, as after firmGuard, answering the members request. */
  async function render(firm: FirmMembership = aFirm, people: Member[] = [owner, member]) {
    const loaded = TestBed.inject(CurrentUserService).load();
    http.expectOne(currentUserUrl).flush(aUser(firm));
    await loaded;

    fixture = TestBed.createComponent(Team);
    // Not whenStable(): an in-flight httpResource request is a pending task, so it would wait forever
    TestBed.tick();
    http.expectOne(membersUrl).flush(people);
    await settle();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const emailInput = () => element().querySelector<HTMLInputElement>('input')!;
  const errors = () =>
    Array.from(element().querySelectorAll('mat-error, .form-error')).map((e) =>
      e.textContent?.trim(),
    );
  /** Each row as [email, expiry, has a renew button]. */
  const items = () =>
    Array.from(element().querySelectorAll('.invitations li:not(.empty)')).map((li) => [
      li.querySelector('.email')?.textContent?.trim(),
      li.querySelector('.expires, .expired')?.textContent?.trim(),
      li.textContent?.includes('Renovar'),
    ]);
  const button = (text: string) =>
    Array.from(element().querySelectorAll('button')).find((b) => b.textContent?.includes(text))!;
  /** Each member row as [name, email, role]. */
  const memberRows = () =>
    Array.from(element().querySelectorAll('.members li')).map((li) => [
      li.querySelector('.name')?.textContent?.replace(/\s+/g, ' ').trim(),
      li.querySelector('.secondary')?.textContent?.trim(),
      li.querySelector('.role')?.textContent?.trim(),
    ]);
  const revokeButton = (email: string) =>
    element().querySelector<HTMLButtonElement>(
      `button[aria-label="Anular la invitación a ${email}"]`,
    )!;

  /** Answers the pending list request. */
  async function respondList(body: Invitation[]) {
    TestBed.tick();
    http.expectOne((r) => r.method === 'GET' && r.url === invitationsUrl).flush(body);
    await fixture.whenStable();
  }

  async function type(value: string) {
    emailInput().value = value;
    emailInput().dispatchEvent(new Event('input'));
    emailInput().dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  function submit() {
    element()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
  }

  /**
   * Lets the component's async code run. Not whenStable(): requests in flight (the list reload, the POST or
   * DELETE) are pending tasks, so it would wait until the test answers them, i.e. forever.
   */
  async function settle() {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  describe('as the owner', () => {
    beforeEach(() => render());

    it('lists the open invitations and when they expire', async () => {
      await respondList([invitation(1), invitation(2, true)]);

      expect(items()).toEqual([
        ['persona1@example.com', 'Caduca el 28/10/2026', false],
        ['persona2@example.com', 'Caducada el 28/10/2026', true],
      ]);
    });

    it('says so when there are no invitations', async () => {
      await respondList([]);

      expect(element().querySelector('.empty')?.textContent).toContain(
        'No hay invitaciones pendientes.',
      );
    });

    it('tells the owner where the person has to sign up', async () => {
      await respondList([]);

      expect(element().textContent).toContain(location.origin);
    });

    it('offers to retry when the list fails to load', async () => {
      TestBed.tick();
      http.expectOne(invitationsUrl).flush(null, { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      button('Reintentar').click();
      await respondList([invitation(1)]);

      expect(items()).toHaveLength(1);
    });

    describe('inviting', () => {
      beforeEach(() => respondList([]));

      it('invites the address, clears the form and reloads the list', async () => {
        await type('  luis@example.com ');
        submit();

        const request = http.expectOne((r) => r.method === 'POST' && r.url === invitationsUrl);
        expect(request.request.body).toEqual({ email: 'luis@example.com' });
        request.flush({ ...invitation(1), email: 'luis@example.com' });
        await settle();

        await respondList([{ ...invitation(1), email: 'luis@example.com' }]);
        expect(emailInput().value).toBe('');
        expect(errors()).toEqual([]);
        expect(items().map(([email]) => email)).toEqual(['luis@example.com']);
      });

      it('rejects an invalid address without calling the API', async () => {
        await type('luis');
        submit();
        await fixture.whenStable();

        expect(errors()).toEqual(['El email no es válido.']);
        http.expectNone((r) => r.method === 'POST');
      });

      it('requires an address', async () => {
        submit();
        await fixture.whenStable();

        expect(errors()).toEqual(['El email es obligatorio.']);
        http.expectNone((r) => r.method === 'POST');
      });

      it("doesn't let the owner invite themselves", async () => {
        await type('ANA@gestoria.es');
        submit();
        await fixture.whenStable();

        expect(errors()).toEqual(['Ya formas parte de la gestoría.']);
        http.expectNone((r) => r.method === 'POST');
      });

      it('shows the API rejecting the address (400)', async () => {
        await type('luis@example.com');
        submit();

        http
          .expectOne((r) => r.method === 'POST')
          .flush(null, { status: 400, statusText: 'Bad Request' });
        await settle();

        expect(errors()).toEqual(['El email no es válido.']);
      });

      it('shows an error when the server fails', async () => {
        await type('luis@example.com');
        submit();

        http
          .expectOne((r) => r.method === 'POST')
          .flush(null, { status: 500, statusText: 'Server Error' });
        await settle();

        expect(errors()).toEqual(['No se ha podido crear la invitación. Inténtalo de nuevo.']);
        expect(emailInput().value).toBe('luis@example.com');
      });
    });

    it('renews an expired invitation by inviting the address again', async () => {
      await respondList([invitation(2, true)]);

      button('Renovar').click();
      const request = http.expectOne((r) => r.method === 'POST' && r.url === invitationsUrl);
      expect(request.request.body).toEqual({ email: 'persona2@example.com' });
      request.flush(invitation(2));
      await settle();

      await respondList([invitation(2)]);
      expect(items()).toEqual([['persona2@example.com', 'Caduca el 28/10/2026', false]]);
    });

    describe('revoking', () => {
      beforeEach(() => respondList([invitation(1)]));

      it('asks first, then revokes and reloads the list', async () => {
        dialogResult = true;

        revokeButton('persona1@example.com').click();
        await settle();

        expect(openDialog).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
        const request = http.expectOne(`${invitationsUrl}/id-1`);
        expect(request.request.method).toBe('DELETE');
        request.flush(null, { status: 204, statusText: 'No Content' });
        await settle();

        await respondList([]);
        expect(items()).toEqual([]);
      });

      it('does nothing when the owner cancels', async () => {
        revokeButton('persona1@example.com').click();
        await settle();

        http.expectNone((r) => r.method === 'DELETE');
      });

      it('reloads the list even if it was already gone (404)', async () => {
        dialogResult = true;

        revokeButton('persona1@example.com').click();
        await settle();
        http
          .expectOne(`${invitationsUrl}/id-1`)
          .flush(null, { status: 404, statusText: 'Not Found' });
        await settle();

        await respondList([]);
      });
    });

    describe('members', () => {
      const removeButton = (name: string) =>
        element().querySelector<HTMLButtonElement>(
          `button[aria-label="Quitar a ${name} de la gestoría"]`,
        );

      beforeEach(() => respondList([]));

      it('lists the people with their role, marking the current user', () => {
        expect(memberRows()).toEqual([
          ['Ana García (tú)', 'ana@gestoria.es', 'Administrador'],
          ['Luis Pérez', 'luis@gestoria.es', 'Miembro'],
        ]);
      });

      it('can remove members but not the owner', () => {
        expect(removeButton('Luis Pérez')).not.toBeNull();
        expect(removeButton('Ana García')).toBeNull();
      });

      it('asks first, then removes the member and reloads the list', async () => {
        dialogResult = true;

        removeButton('Luis Pérez')!.click();
        await settle();

        expect(openDialog).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
        const request = http.expectOne(`${membersUrl}/member-id`);
        expect(request.request.method).toBe('DELETE');
        request.flush(null, { status: 204, statusText: 'No Content' });
        await settle();

        http.expectOne(membersUrl).flush([owner]);
        await settle();
        expect(memberRows()).toHaveLength(1);
      });

      it('does nothing when the owner cancels', async () => {
        removeButton('Luis Pérez')!.click();
        await settle();

        http.expectNone((r) => r.method === 'DELETE');
      });

      it('reloads the list even if the member was already gone (404)', async () => {
        dialogResult = true;

        removeButton('Luis Pérez')!.click();
        await settle();
        http
          .expectOne(`${membersUrl}/member-id`)
          .flush(null, { status: 404, statusText: 'Not Found' });
        await settle();

        http.expectOne(membersUrl).flush([owner]);
      });
    });
  });

  describe('as a member', () => {
    beforeEach(() =>
      render({ ...aFirm, role: 'Member' }, [
        { ...owner, isCurrentUser: false },
        { ...member, isCurrentUser: true },
      ]),
    );

    it('sees the people of the firm', () => {
      expect(memberRows().map(([name]) => name)).toEqual(['Ana García', 'Luis Pérez (tú)']);
    });

    it("can't invite nor remove anyone, and doesn't ask for the invitations", () => {
      http.expectNone(invitationsUrl);
      expect(element().querySelector('form')).toBeNull();
      expect(element().querySelector('button[aria-label^="Quitar"]')).toBeNull();
      expect(element().textContent).not.toContain('Invitaciones pendientes');
    });
  });

  it('shows the email when the person has no name yet', async () => {
    await render(aFirm, [{ ...member, name: null }]);
    await respondList([]);

    expect(memberRows()).toEqual([['luis@gestoria.es', undefined, 'Miembro']]);
  });
});
