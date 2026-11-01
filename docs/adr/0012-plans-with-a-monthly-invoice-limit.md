# ADR 0012: Plans with a monthly invoice limit, a free trial and Stripe for the payments

- Status: accepted
- Date: 2026-10-31

## Context

Every invoice Plumy reads costs money: Document Intelligence per page, and the language model when the supplier is
new. A flat price per firm would make the busiest firms the least profitable, so the price has to follow the volume.
A firm also has to be able to try Plumy before paying anything, or nobody would get past the sign-up.

Charging means cards, VAT, receipts, failed payments and cancellations. None of that is Plumy's product, and handling
card data ourselves brings PCI DSS with it.

## Decision

- **Plans by number of invoices a month.** Basic (300) and Pro (1,500). The limit counts every invoice uploaded in
  the period, rejected ones too: reading it already cost what the limit covers. A duplicate file is refused before it
  is stored, so it doesn't count. The prices live in Stripe, not in the code: changing one is not a deploy.
- **When the limit is reached, uploads stop (402) until the firm changes plan.** No overage charges: a gestoría
  should know what it pays each month. Two uploads racing for the last place can both get in; a lock to make the limit
  exact isn't worth it.
- **A 14-day trial with 50 invoices, run by Plumy, with no card.** Every new firm starts it; Stripe only appears when
  the owner picks a plan. The firms that existed before this ADR start the same trial on the day it ships.
- **Only uploading depends on the plan.** Reading, reviewing, correcting and exporting the invoices already in Plumy
  never stop, not even for a firm that stopped paying: they are its books, and holding them hostage is not a
  business model.
- **Four statuses of our own**: Trialing, Active, PastDue (a payment failed and Stripe is retrying: the firm keeps
  working) and Canceled (no uploads). Stripe's statuses map onto them, so the rest of the code never sees Stripe's.
- **Stripe-hosted pages for everything with a card**: Checkout to subscribe, the Customer Portal to change plan,
  update the card, see the receipts or cancel. Card data never touches Plumy (PCI SAQ A).
- **Stripe's webhooks are the source of truth**, not the redirect back from Checkout: the plan and the period are
  stored when a signed event says so. The redirect only tells the screen to look again.

## Consequences

- The period whose uploads count is the trial or Stripe's billing month, stored on the firm. A paid period that has
  passed with no event yet keeps counting from its start (a renewal is on its way), which can only be stricter.
- Stripe's API key and the webhook signing secret are the first secrets Plumy has that Entra ID can't replace: they
  bring Key Vault in (referenced from Container Apps), as planned in the tech stack.
- Stripe runs in a sandbox only. Charging for real needs a legal entity to charge as, Spanish VAT on the receipts
  (Stripe Tax) and terms of service; none of that is code.
- The webhook doesn't trust the event's copy of the subscription: it fetches the subscription from Stripe and stores
  that. Events come late, twice or out of order, and this way any number of them in any order leaves the same
  result, without a table of processed events. A subscription that ended while the firm pays a newer one is ignored.
- The plans are found in Stripe by the prices' lookup keys (`plumy_basic_monthly`, `plumy_pro_monthly`): a new price
  takes the key over and nothing is deployed.
- Without a Stripe key the API runs with payments off (503), so the rest of Plumy needs no Stripe account.
- Per-company limits, a cheaper yearly plan or overage pricing are later changes to the catalog, not to this model.
