# ADR 0006: Validate read invoices with deterministic rules, stored as warnings

- Status: accepted
- Date: 2026-10-24

## Context

Document Intelligence reads what is printed, and can misread it; the invoice itself can be wrong, addressed to
another company or uploaded twice. The accountant needs to know which invoices to look at before booking them.
These checks are exact rules (a NIF's check digit, a sum), so they belong in code, not in a model
(`docs/product.md`: AI only for fuzzy problems).

## Decision

- **A pure validator in the domain** (`InvoiceValidator`): the read data, the company's NIF, the earlier invoice it
  may duplicate and today's date in; a list of issues out. Easy to test with plain values.
- **Run once, when the invoice is read**, in the worker, and stored with the data (`Issues` as a JSON array of names,
  plus `DuplicateOfInvoiceId`). They describe what was read, so they change only when the data does; the list reads
  them with no extra queries.
- **Warnings, not errors.** Every issue still lets the invoice through; the list shows it as *Revisar* with the
  reasons. Blocking would only push people to work around the tool.
- **The checks:**
  - Missing supplier NIF, invoice number, date or total.
  - A Spanish supplier NIF/CIF/NIE with a wrong check digit (the existing `TaxId`). An `ES` prefix is ignored; any
    other country prefix is foreign and not checked.
  - The customer NIF (now extracted) is not the company's: uploaded to the wrong company. No customer NIF is fine
    (receipts).
  - A date after tomorrow.
  - Base + VAT ≠ total, within 2 cents, **unless a usual IRPF withholding (1, 2, 7, 15 or 19%) explains it**:
    professionals and rentals invoice base + VAT − IRPF, and a naive sum would flag every one of them.
  - VAT above 26.7% of the base (21% plus the 5.2% equivalence surcharge, with rounding), or of the opposite sign.
  - Possible duplicate: an earlier invoice of the same company, already read, with the same supplier NIF and number.
    Only the later one is flagged, pointing to the first. The file hash already rejects the exact same file; this
    catches a second scan or photo of the same paper.

## Alternatives

- **Check each VAT rate (21/10/4/0)**: only the totals are read, not the breakdown per rate, and an invoice mixing
  rates averages between them (15% is fine). Only the upper bound is certain.
- **Validate on every read of the list**: always current, but the duplicate check is a query per invoice.
- **Ask the model to check the invoice**: slower, paid, and less reliable than arithmetic.

## Consequences

Good:

- The accountant sees at a glance which invoices need a look, and why.
- No AI, no cost, fully tested with plain values.

Bad:

- Invoices read before this change have no issues stored: they show as fine.
- With a 1% or 2% withholding, a total off by that much on a small invoice passes as a withholding.
- Two copies of an invoice read at the same moment can miss each other.
- Issues aren't recalculated when the data is corrected, because data can't be corrected yet; the review step will
  need to.
