/** Mirrors Plumy.Application.Firms.InvitationResponse: an invitation as the firm's owner sees it. */
export interface Invitation {
  id: string;
  email: string;
  /** ISO 8601. */
  createdAt: string;
  /** ISO 8601. */
  expiresAt: string;
  /** Decided by the server's clock. Expired invitations can be renewed by inviting the address again. */
  isExpired: boolean;
}

export const invitationsUrl = '/api/firm/invitations';

/** Same limit as EmailAddress.MaxLength in the domain. */
export const maxEmailLength = 254;
