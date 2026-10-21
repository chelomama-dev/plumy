# ADR 0002: Firm invitations matched by verified email, without sending emails

- Status: accepted
- Date: 2026-10-21

## Context

A firm's owner needs to add their team. Users sign up on their own in Entra External ID (ADR 0001), so a new
user arrives at Plumy with an `oid` and an `email` claim but no firm. Plumy has to decide which firm they join.

The usual design emails a link with a secret token. That needs an email service (domain verification, SPF and
DKIM, templates, bounces) before the feature is useful, and the token in the link is a credential that can leak
through forwarded emails or browser history.

## Decision

- An invitation is a row tied to a firm and an **email address** (trimmed and lowercased). It has no secret.
- Whoever signs in with that address sees the invitation (in `GET /api/me`) and can accept it. Accepting checks
  that the invitation was sent to the email in the access token. Entra verifies that address with a one-time
  code at sign-up, so the claim proves the user controls it.
- Plumy doesn't send emails yet: the owner tells the person to sign up at app.getplumy.com.
- Invitations expire after 7 days. Inviting an address again renews its open invitation instead of creating a
  second one (a filtered unique index enforces one open invitation per address and firm).
- Only the owner manages invitations. A user belongs to one firm at most (unique index on `oid`), so accepting
  fails for users who already have one.
- Invitations belong to the `Firm` aggregate. Accepting one adds a `FirmUser` with the Member role.

## Consequences

Good:

- No email infrastructure and no secret to protect. A leaked invitation id is useless without the account.
- Sending an email later only adds a notification. The acceptance rules stay the same.

Bad:

- The invitee only learns about it from the owner until emails are sent (planned with Azure Communication
  Services Email).
- It relies on the `email` claim being verified. Any identity provider added to the tenant later (Google,
  Microsoft accounts, SAML) must also issue verified addresses. Otherwise this check must change.
- The owner can't see who already belongs to the firm by email: members are stored by `oid` only. A members list
  comes in a later change. *Update 2026-10-21: done. `FirmUser` keeps a copy of the name and email from the
  token, refreshed by `GET /api/me`, and the Team page lists everyone in the firm.*

## Alternatives considered

- **Emailed link with a one-time token**: works even if the person signs up with another address. Rejected for
  now because of the email infrastructure it needs first.
- **Invitations in Entra (B2B invitation API)**: meant for guest users of workforce tenants, not for customers
  signing up to an external tenant, and it would put Plumy's tenancy in Entra.
