# ADR 0010: Measure the AI against what people approved

- Status: accepted
- Date: 2026-10-29

## Context

Plumy uses AI twice on each invoice: Document Intelligence reads its fields (ADR 0004) and, for a supplier with no
history, a language model suggests the ledger account (ADR 0005). Until now nobody could say how well either does.
Without that, two next steps are guesses:

- **Auto-approving** invoices the AI is sure about needs a confidence above which it is (almost) never wrong.
- **The review screen calls a field doubtful under 0.8** of confidence. That number came from Document Intelligence's
  docs, not from our invoices.

The data is already there. The suggestion is kept after a person confirms an account (`LedgerAccountSuggestion`, with
its source), the reader's confidence per field is stored (`FieldConfidence`), and so are the fields a person changed
(`CorrectedFields`).

## Decision

- **The right answer is what an approved invoice ended up with.** Approving means a person went through it field by
  field, so its account and its data are the reference. Unreviewed invoices may still change, and rejected ones are
  often not invoices at all: neither counts.
- **Accounts: suggested vs confirmed, by source.** The share of AI suggestions kept is reported apart from the
  supplier-history ones (repeating last time's account is right almost always, and would flatter the AI). Approved
  invoices with no suggestion (the AI declined or failed, or a person was faster) are counted on their own.
- **Fields: read, corrected and missed per field**, only over invoices that came with confidences (a reader without
  them can't tell a correction from a miss). **And by confidence band** (0–50, 50–80, 80–90, 90–95, 95–100 %): what
  share of the fields read with that confidence a person had to correct. That is the table that sets both the
  "doubtful" line and, later, the auto-approval one.
- **Computed on request, no stored metrics.** `GET /api/invoices/ai-accuracy` (optional company and approval dates,
  UTC days) projects five columns of the newest 50,000 approved invoices and aggregates in memory. JSON columns
  (confidences, corrected fields) can't be grouped in SQL without `OPENJSON`, and a firm has thousands of invoices, not
  millions. Nothing to keep in sync, and a fix to the formula applies to the past too.
- **A page for the firm** ("Precisión IA"), any member: three numbers, the fields worst first and the confidence bands.

## Consequences

- **"Accepted" is not "right".** A reviewer tends to keep what is suggested (automation bias), so these numbers are an
  upper bound. The page says so. A blind sample (classify without showing the suggestion) would measure it properly;
  not worth it at this size.
- **A field corrected and then put back stays "corrected"**: `CorrectedFields` only grows while the invoice is
  unreviewed. Rare, and it errs on the side of blaming the AI.
- **No index for this query**: it scans the firm's approved invoices through the tenant filter. Fine for now; an index
  on `(FirmId, ReviewStatus, ReviewedAt)` is the fix if it gets slow.
- Accuracy over time (is it getting better?) and per company are one query parameter away; a chart of the trend is
  left for when there is enough history to draw one.
