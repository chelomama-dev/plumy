/** Mirrors Plumy.Application.Firms.FirmMembershipResponse. */
export interface FirmMembership {
  id: string;
  name: string;
  role: 'Owner' | 'Member';
}

/** Mirrors Plumy.Application.Firms.PendingInvitation: a firm that invited the signed-in user. */
export interface PendingInvitation {
  id: string;
  firmName: string;
  /** ISO 8601. */
  expiresAt: string;
}

/** Mirrors Plumy.Application.Firms.GetCurrentUser.CurrentUserResponse. */
export interface CurrentUser {
  name: string | null;
  email: string | null;
  /** Null until the user creates or joins a firm. */
  firm: FirmMembership | null;
  /** Invitations to the user's email they can accept. Always empty once they have a firm. */
  invitations: PendingInvitation[];
}

export const currentUserUrl = '/api/me';
export const firmsUrl = '/api/firms';
export const acceptInvitationUrl = (id: string) => `/api/invitations/${id}/accept`;

/** Same limit as Firm.MaxNameLength in the domain. */
export const maxFirmNameLength = 200;
