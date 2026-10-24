# ADR 0007: Export read invoices to Excel and CSV, read-only

- Status: accepted
- Date: 2026-10-24

## Context

The MVP ends with the accountant taking the invoices out of Plumy into their accounting software
(`docs/product.md`). Each program (A3, Sage, Holded...) has its own import format; that is out of scope for now.
What every firm can use today is a spreadsheet they review and import, or a CSV.

## Decision

- **`GET /api/invoices/export`**, for one company or all, by issue date (inclusive), as `xlsx` (default) or `csv`.
  Only read invoices; any member of the firm can export.
- **One row per invoice**: company, date, number, supplier and NIF, base, VAT, total, currency, the account (the
  confirmed one, or the suggested one with a status column saying which), the issues in words, file name and id.
- **Read-only.** Exporting doesn't mark invoices as exported: it can be repeated, and nothing breaks if a download
  is lost. Tracking what was already booked belongs to the approval step, which comes later.
- **Excel with ClosedXML** (MIT): amounts and dates are real numbers and dates, bold frozen header, filters on.
  EPPlus was left out: its licence has been commercial since version 5.
- **CSV the way Excel in Spanish opens it**: semicolons, decimal comma, `dd/MM/yyyy`, CRLF, UTF-8 with BOM (without
  it Excel reads the accents as Windows-1252). Written by hand: it's a few lines and every choice is explicit.
- **No formula injection.** Supplier names come from documents anyone can write. In the CSV, text starting with `=`,
  `+`, `-`, `@`, tab or CR gets a leading apostrophe (OWASP's advice). In Excel, text cells are always stored as text,
  never as formulas. Amounts are written by us and never escaped, so a credit note stays a negative number.
- **Built in memory, capped at 10,000 invoices** per export; beyond that, a 400 asks for a shorter period. A firm's
  month is far below it. Streaming can come later if needed.
- `Cache-Control: no-store`: these are a company's books.

## Consequences

Good:

- The MVP's loop is closed: upload, read, classify, check, export.
- The same columns for both formats (`InvoiceExportColumns`), in Spanish, for Spanish accountants.

Bad:

- No program-specific format yet: the accountant still maps columns on import.
- Without an "exported" mark, nothing stops an invoice being booked twice from two exports.
