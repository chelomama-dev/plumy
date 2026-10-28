# ADR 0008: A person reviews each invoice (corrects, approves or rejects it) before it is booked

- Status: accepted
- Date: 2026-10-26

## Context

The machine does three things with an invoice: Document Intelligence reads it, the rules check it (ADR 0006) and the
history or a model suggests its account (ADR 0005). All three can be wrong, and until now nobody could fix the data
or say "this one is fine": the export took every read invoice, suggestions included. `docs/product.md` has always
had a review step (approve / correct / reject) between reading and exporting; this is it, on the API side. The
screen comes next.

## Decision

- **A review status apart from the extraction status**: `ReviewStatus` is `Unreviewed`, `Approved` or `Rejected`.
  `ExtractionStatus` is the machine's progress, `ReviewStatus` a person's decision; mixing them in one enum would
  give states like "approved but still processing". Same split as the suggestion against the confirmed account.
- **Correct = send the whole invoice** (`PUT /api/invoices/{id}/data`). The values go through
  `ExtractedInvoiceData.Create`, so a typed NIF is tidied like a read one and compares the same way. The checks run
  again on the new values, in the API now (`InvoiceChecks`, shared with the worker): a fixed total stops being
  flagged, and a corrected number can turn it into a duplicate. A value that doesn't fit is a 400, never shortened
  or dropped without a word.
- **The fields a person changed are kept** (`CorrectedFields`, JSON array of names). The screen shows them as
  corrected instead of showing the reader's doubt about a value that is no longer the reader's.
- **The reader's confidence is kept per field** (`FieldConfidence`, a JSON object, 0 to 1), as Document
  Intelligence gives it; the currency takes the total's, the description its least sure line's. Only for fields
  that have a value. The threshold for highlighting lives in the front: it is a display choice, and it can be tuned
  without touching stored data.
- **Approve needs a confirmed account**; the issues don't block it (they are warnings, and the person has seen
  them). **Reject needs a reason** (free text, up to 300 characters) and also works on an invoice that couldn't be
  read: an unreadable file is often not an invoice. Both record who and when.
- **Approved and rejected invoices are locked**: no correction, no account change, no retry. Reopening (status back
  to `Unreviewed`) unlocks them. One endpoint, `PUT /api/invoices/{id}/review` with the status wanted: the same
  request twice gives the same result, and rejected → approved needs no reopening in between.
- **Only approved invoices are exported** (amends ADR 0007). With that, the exported account is always a confirmed
  one, so the "account status" column (confirmed / suggested by AI / by history) is gone.
- **Rejected invoices teach nothing and duplicate nothing**: they are left out of the account history (ADR 0005) and
  of the duplicate check. A rejected invoice "of the wrong company" would otherwise lend its account to the next
  invoice of that supplier, or make the right copy look like a duplicate.
- Any member of the firm can review, as with confirming the account.

## Alternatives

- **Keep the reader's values next to the person's** (two copies of the data): a full audit of what changed, but
  every query (export, duplicates, history) would have to pick one. The list of corrected fields says what the screen
  needs; the original values can come back with an audit log if a firm asks for one.
- **Separate approve / reject / reopen endpoints** (`POST .../approve`): clearer verbs, but three endpoints that
  are not idempotent for one piece of state.
- **Export every invoice but the rejected ones, with a review column**: keeps the old behaviour, but lets unchecked
  data reach the books, which is what the review is there to stop.
- **Approve automatically when every check passes and the confidence is high** (as `product.md` sketches): left for
  later, once there is real data to pick a threshold from.

## Consequences

Good:

- The export now means "what a person checked". The flow is the one in `product.md`, end to end.
- A correction re-runs the checks, which ADR 0006 left pending.
- Corrections and confirmed accounts are the data needed to measure how good the reader and the suggestions are.

Bad:

- Invoices that already exist are all `Unreviewed` and have no confidence stored: until someone approves them, the
  export comes out empty.
- Two people reviewing the same invoice at once: the last save wins (there is no concurrency token yet). Solved
  by ADR 0009.
- The corrected fields say *what* was changed, not *from what*.
