# ADR 0011: Approve the invoices Plumy is sure about, if the firm wants it

- Status: accepted
- Date: 2026-10-30

## Context

Every invoice waits for a person to approve it (ADR 0008), even one from a supplier the company books every month,
read without a single doubt. That review is most of the work Plumy is meant to save. ADR 0010 made it measurable how
often a field read with a given confidence had to be corrected: the number a line for approving unseen can rest on.

Approving with nobody looking is also the riskiest thing Plumy can do: a wrong invoice goes to the books. So the
question is not only "when" but "who decides", and how that choice stays honest with the accuracy figures.

## Decision

- **Off unless the firm turns it on, and only its owner can.** Two settings on the firm: on/off and a minimum
  confidence (0.8 to 1; the screen offers 80, 90 and 95 %, the edges of ADR 0010's confidence bands, so the report
  says exactly how often a field above each line was corrected). Default 95 %. Changing it affects the invoices read
  from then on; the ones already waiting stay as they are.
- **Approved by the worker, right after the account is suggested**, in the same save: a redelivered message never
  finds an invoice suggested but not yet considered.
- **Only an invoice with nothing to look at** (`Invoice.CanBeApprovedAutomatically`), all of:
  - No issue at all (ADR 0006): no possible duplicate, amounts that add up, a valid NIF, a date, a number...
  - The account comes from the **supplier's history**: the one a person confirmed last time for this supplier and
    company. **Never the language model's suggestion**: ADR 0010 reports those apart because they are wrong far more
    often, and an AI guess booked unseen is exactly the failure this must not have.
  - Every **booked field** (supplier NIF, number, date, base, VAT, total) read, with at least the firm's confidence.
    The name, currency and description are not booked, so a doubt about them doesn't block it. A reader that gives
    no confidence never qualifies.
  - Untouched: no correction, no account chosen, not reviewed.
- **Marked as Plumy's, not a person's.** `ApprovedAutomatically` on the invoice, no `ReviewedBy`, and the account it
  takes has no `LedgerAccountConfirmedBy`. A person can reopen it like any other; approving it again makes it theirs
  (and the account too).
- **It doesn't grade itself.** Automatically approved invoices are left out of the accuracy report (only counted, so
  the page can say how many), and their accounts are not supplier history. Otherwise the AI would mark its own work
  and the history would echo itself: one person's choice repeated forever with no one confirming it again.

## Consequences

- With the defaults nothing changes: a firm has to look at the numbers and turn it on.
- Fewer invoices reviewed by a person means fewer samples in the accuracy report. The report keeps working on what is
  left, and reopening an automatically approved invoice to check it puts it back in.
- **Spot checks are not built in.** The list marks these invoices ("Aprobada sola") and a person can open and reopen
  any of them; sampling a share of the would-be automatic approvals for review is the obvious next step if a firm asks
  for it.
- The rule is all-or-nothing per invoice and the same for every company of the firm. A per-company switch, or a
  limit on the amount, are one column each if a firm needs them.
- The line is chosen from three values on screen but stored as a number, so finer lines need no migration.
