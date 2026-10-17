# ADR 0001: Authentication with Microsoft Entra External ID

- Status: accepted
- Date: 2026-10-17

## Context

v0.3 adds login and multi-tenancy. Plumy's users are accounting firm staff (admins and accountants) and
employees of their client companies (see `docs/product.md`). The app stores invoices and tax data of third
parties, so a breach is costly (GDPR), and the project has a monthly budget of 5 €.

The two options considered in `docs/tech-stack.md` were ASP.NET Core Identity and Microsoft Entra External ID.

## Decision

Use **Microsoft Entra External ID** (an external tenant) as the identity provider, with OpenID Connect and
OAuth 2.0:

- The Angular app is a public client (SPA, authorization code flow with PKCE) and gets access tokens for the API.
- The API validates JWT bearer tokens and requires an authenticated user by default. Only the health
  endpoints stay anonymous.
- Sign-in method: email + password. MFA (email one-time code) and social or Microsoft 365 sign-in can be added
  later in the tenant without code changes.
- Entra answers *who the user is*. Tenancy (which firm a user belongs to), roles and the client company scope
  live in Plumy's database, keyed by the user's object id (`oid` claim).

## Consequences

Good:

- No password hashes in Plumy's database. Password policy, reset, account lockout, MFA and attack detection
  come from Entra.
- Standard protocols (OIDC, OAuth 2.0, JWT), so the API doesn't depend on the identity provider beyond
  configuration.
- Free up to 50,000 monthly active users.

Bad:

- The sign-in page is hosted on `<tenant>.ciamlogin.com`. A custom domain (e.g. `login.getplumy.com`) requires
  Azure Front Door, which has a fixed monthly fee. Accepted for the MVP; company branding is applied instead.
- The external tenant can't be fully managed with Bicep. It is created once in the Entra admin center, and the
  app registrations are created with an idempotent script.
- Local development and tests depend on the external tenant. API tests use their own token issuer instead.
- Invitations (a firm admin adding a user) are implemented by Plumy on top of Entra's self-service sign-up.

## Alternatives considered

- **ASP.NET Core Identity**: everything in our code and database, simpler local development and passkey
  support in .NET 10. Rejected because Plumy would own password storage, reset emails, MFA and lockout, and
  `MapIdentityApi` issues its own token format instead of standard JWTs.
