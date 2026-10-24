# ADR 0004: Read invoices with Document Intelligence's prebuilt invoice model

- Status: accepted
- Date: 2026-10-24

## Context

The worker from [ADR 0003](0003-invoice-extraction-in-a-background-worker.md) runs with a fake reader. It needs a
real one that reads the supplier, its NIF, the number, the date and the amounts out of Spanish PDFs and photos,
costs little at this volume, and runs in France Central, where everything else is.

## Decision

- **Azure Document Intelligence v4.0, `prebuilt-invoice`** (API 2024-11-30, GA; SDK `Azure.AI.DocumentIntelligence`
  1.0.0). It already knows invoices: no training, no prompt, the same fields every time.
- **Locale `es-ES`** as a hint, so `03/04/2026` is 3 April and `1.234,56` is a thousand.
- **Mapping.** `VendorName`, `VendorTaxId`, `InvoiceId`, `InvoiceDate`, `SubTotal`, `TotalTax`, `InvoiceTotal`; the
  currency is the total's. A field that is missing or comes with another type is left empty, never guessed or
  re-parsed. Checking the values (a valid NIF, base + VAT = total) is the review step, not this one.
- **Errors.** The service refusing the file (400, 413, 415, or an analysis failing with `InvalidContent`) and a file
  with no invoice in it fail the invoice with a message for the user. Everything else (429, 5xx, auth) is
  transient: the SDK retries a few times, then the queue retries (ADR 0003).
- **Keyless.** The account has local auth disabled and a custom subdomain (Entra ID tokens need it). The worker's
  identity is *Cognitive Services User* on that account only.
- **Free tier (F0)** for now: 500 pages a month, but only the first 2 pages of each file and files up to 4 MB
  (uploads allow 10 MB; a bigger file fails as unreadable). One parameter, `documentIntelligenceSku`, moves it to
  S0 when real customers arrive.
- **The fake reader stays** for local development and tests, chosen explicitly (`InvoiceExtraction:Extractor`). There
  is no default: the worker refuses to start rather than read invoices with the wrong one.

## Alternatives

- **Content Understanding** (the newer Foundry service, GA): also has an invoice analyzer, but extracting fields needs
  a model deployed in Foundry, billed in tokens on top, and it isn't available in France Central.
- **An LLM with vision** (GPT in Foundry) and a JSON schema: flexible, but its answers vary between runs, it can make
  values up, and it costs more per page for a standard invoice. Better kept for what Document Intelligence doesn't
  do: suggesting the accounting account, which comes later.
- **Our own OCR + rules**: months of work to get what the prebuilt model already does.

## Consequences

Good:

- Real data with no training and no secrets to keep.
- Free while testing; the cost per page is known when it stops being free.
- The reader stays behind `IInvoiceExtractor`: changing it touches one class and its registration.

Bad:

- On F0, invoices longer than 2 pages lose whatever is on page 3 onwards (often the totals).
- Deleting `rg-plumy` leaves the account soft-deleted for 48 hours, and a new deployment fails until it is purged
  (see `infra/README.md`).
- Field confidence isn't stored yet; the review step will want it to flag doubtful values.
