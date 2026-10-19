import { CurrentUser, FirmMembership } from './current-user';

export const aFirm: FirmMembership = { id: 'firm-id', name: 'Gestoría López', role: 'Owner' };

export function aUser(firm: FirmMembership | null = aFirm): CurrentUser {
  return { name: 'Ana García', email: 'ana@gestoria.es', firm };
}
