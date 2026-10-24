# ADR 0005: Suggest ledger accounts with rules first, then a language model in Foundry

- Status: accepted
- Date: 2026-10-24

## Context

Once an invoice is read ([ADR 0004](0004-read-invoices-with-document-intelligence.md)), the accountant has to book
it to an account of the Spanish chart of accounts (PGC): electricity to 628, a lawyer to 623, a laptop to 217. That
account isn't written on the invoice, so Document Intelligence can't read it: it has to be decided. It is the core of
the product (`docs/product.md`): suggest the account, let a person confirm or correct it, and learn from that.

Most invoices come from suppliers the company already has: the answer is whatever was booked last time. Only new
suppliers really need judgment.

## Decision

- **Rules first.** If a person already confirmed an account for the same supplier (same NIF) in the same client
  company, that account is suggested again. No model call: free, instant and what the accountant expects.
- **A language model for the rest**: `gpt-5.4-mini` (version 2026-03-17) in Microsoft Foundry, through the OpenAI
  v1 API with the official `OpenAI` .NET library (2.14.0). It gets the supplier's name, what was bought (the lines'
  descriptions, newly extracted for this) and the amounts, plus the company's 20 most recently confirmed invoices as
  examples, so it follows how this firm books things.
- **The answer can only be a valid account.** Structured outputs with a strict JSON schema whose `code` is an `enum`
  of the catalog's codes (or `null`, "I can't tell"). The code is checked again against the catalog anyway. No
  answer is better than a wrong one: a person picks the account then.
- **The catalog lives in code** (`ChartOfAccounts`): 22 PGC accounts of groups 6 and 2 at the 3-digit level every
  firm shares. Each firm's subaccounts (6280001) are an export concern for later.
- **Suggestion and confirmation are kept apart.** The suggestion (code, source, reason, when) stays on the invoice
  after a person confirms an account, so accuracy can be measured (suggested vs. confirmed). Only confirmed accounts
  feed the history; an unconfirmed suggestion never teaches anything.
- **Runs in the worker, after extraction, in the same message.** The extracted data is saved first; if the model
  then fails transiently the message is retried and the retry only asks for the suggestion again (the file isn't
  read again). On the last attempt the invoice is left read, without a suggestion. No new queue.
- **Data Zone Standard deployment** in France Central: requests are processed inside the EU data zone. The
  subscription has no Global Standard quota for this model anyway (checked 2026-10-24).
- **Keyless.** AIServices account with local auth disabled; the worker's identity is *Cognitive Services User* on
  it, the role Microsoft documents for keyless inference and the one the pipeline could already assign (no bootstrap
  change). Token scope `https://ai.azure.com/.default`, as documented (configurable, in case it changes).
- **No reasoning effort set.** `gpt-5.4-mini` then doesn't reason (its default, `none`): enough to pick from a short
  list, and faster and cheaper. The SDK still marks that setting as experimental.
- **Data minimization and prompt injection.** No NIF (a self-employed supplier's NIF is personal data) and no
  invoice number go to the model. The invoice travels as JSON in its own message, and the instructions say it is
  data, not orders. The worst a malicious invoice can do is get a wrong account suggested, which a person reviews.
- **A fake classifier** for local development and tests, chosen explicitly, as with the extractor.

## Alternatives

- **Only rules** (supplier → account, keywords): covers repeat suppliers, which the rule above already does, but
  keyword lists for new ones are endless and brittle.
- **Only the model**: pays for and waits on a call that the history answers for free, and can disagree with what the
  accountant chose last time.
- **A model of our own** (classification trained on confirmed invoices): needs thousands of labelled invoices we
  don't have. Worth revisiting once there is data; the suggested-vs-confirmed pairs are that data.
- **Global Standard**: about 10% cheaper, but routes requests anywhere in the world, and there's no quota for it.
- **Another model**: `gpt-5-mini` retires in February 2027; `gpt-4.1-mini` and `gpt-4o-mini` are legacy or
  deprecating. `gpt-5.4-mini` is GA until 2027-09-21. The deployment upgrades itself when its version retires
  (`OnceCurrentVersionExpired`) instead of stopping.

## Consequences

Good:

- Repeat suppliers (most invoices) cost nothing and are instant; new ones cost about $0.0015 each (about 1,500
  input and 60 output tokens at $0.825 / $4.95 per million, Data Zone, October 2026).
- Every confirmation makes the next suggestions better without retraining anything.
- The classifier stays behind `ILedgerAccountClassifier`: changing model or provider touches one class and its
  registration.

Bad:

- Unlike Document Intelligence, the model has no free tier: every new supplier is paid for.
- Invoices read before this change have no suggestion; a person picks their account.
- Deleting `rg-plumy` leaves this account soft-deleted for 48 hours too (see `infra/README.md`).
- The history matches suppliers by NIF only: an invoice whose NIF wasn't read always goes to the model.
