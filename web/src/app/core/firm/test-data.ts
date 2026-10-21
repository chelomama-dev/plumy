import { CurrentUser, FirmMembership, PendingInvitation } from './current-user';

export const aFirm: FirmMembership = { id: 'firm-id', name: 'Gestoría López', role: 'Owner' };

export const anInvitation: PendingInvitation = {
  id: 'invitation-id',
  firmName: 'Asesoría Martínez',
  expiresAt: '2026-10-28T09:00:00+00:00',
};

export function aUser(
  firm: FirmMembership | null = aFirm,
  invitations: PendingInvitation[] = [],
): CurrentUser {
  return { name: 'Ana García', email: 'ana@gestoria.es', firm, invitations };
}
