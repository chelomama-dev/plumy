/** Mirrors Plumy.Application.Firms.MemberResponse. */
export interface Member {
  id: string;
  /** Null until the person signs in again after profiles started being stored. */
  name: string | null;
  email: string | null;
  role: 'Owner' | 'Member';
  isCurrentUser: boolean;
}

export const membersUrl = '/api/firm/members';
