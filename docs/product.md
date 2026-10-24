# Product definition

Status: draft (2026-10-04).

## Vision

**Plumy turns the supplier invoices of an accounting firm's clients (in any format) into validated, accounting-classified data, ready to import into their accounting software, with human review only where needed.**

Target market: Spanish accounting firms (*gestorías*) and, later, SMEs managing their own bookkeeping.

### Why it still matters with mandatory B2B e-invoicing

Spain's RD 238/2026 makes structured B2B e-invoices (Facturae, UBL, EDIFACT, CEFACT) mandatory (~2027 for companies > €8M, ~2028 for the rest). Plumy is therefore **not** an OCR product. The core value is validation + accounting classification + export, which structured invoices do not solve. OCR remains necessary for receipts (B2C), foreign invoices and the transition period.

## Users

| Role | Description |
|---|---|
| Firm admin | Manages the firm (tenant), its users and client companies |
| Firm accountant | Uploads, reviews, approves and exports invoices |
| Client user | Employee of a client company; uploads its invoices through the portal |

## Domain model (initial)

- **Firm** (*Gestoría*): the tenant.
- **ClientCompany**: a company the firm keeps the books for (name, tax ID / NIF).
- **User**: belongs to a firm, optionally scoped to one client company.
- **Supplier**: invoice issuer, per client company (NIF, name, default ledger account).
- **Invoice**: number, issue date, supplier, recipient, taxable base, VAT breakdown, total, status, source document.
- **InvoiceLine**: description, quantity, unit price, VAT rate, amount.
- **VatBreakdown**: rate, base and amount (an invoice can mix VAT rates).
- **LedgerAccount**: Spanish chart of accounts (*Plan General Contable*) account, e.g. 6280, 6230.
- **Document**: the uploaded file (blob), with extraction results and per-field confidence.
- **ClassificationFeedback**: user corrections, used to improve future suggestions.

## Invoice lifecycle

```
Uploaded → Processing → (Validation + Classification) → PendingReview → Approved → Exported
                                                     └─(all checks pass, high confidence)─┘
Failed (extraction error) → can be retried or entered manually
Rejected (not an invoice, duplicate, wrong client)
```

## Validation rules (deterministic code, not AI)

- Spanish NIF/CIF/NIE check digit (issuer and recipient).
- Recipient NIF matches the client company the invoice was uploaded to.
- Taxable base + VAT amounts = total (with rounding tolerance).
- Valid Spanish VAT rates (21 / 10 / 4 / 0) and amounts consistent with rates.
- Duplicate detection (same supplier + invoice number + client company).
- Mandatory fields present; dates not in the future.

## Where AI is used

- **Extraction**: Azure AI Document Intelligence (prebuilt invoice model) → normalized fields with confidence.
- **Classification**: the supplier's history first (the account a person last confirmed for that NIF in that
  company); for a new supplier, an LLM in Microsoft Foundry (`gpt-5.4-mini`) picks from the PGC catalog, with the
  company's confirmed invoices as examples. See [ADR 0005](adr/0005-suggest-ledger-accounts-with-rules-then-a-language-model.md).
- Rule of thumb: AI only for fuzzy problems; everything deterministic stays in code.

## MVP scope (v0.3)

In scope:
- One firm (tenant) with users and client companies (two-level multi-tenancy).
- Upload PDF / image, by firm users and client users.
- Background extraction (queue + worker).
- Validation rules above.
- Ledger account suggestion with learning from corrections.
- Review screen: document side by side with fields, confidence highlighting, approve / correct / reject.
- Export to CSV / Excel.

Out of scope (later versions):
- Structured e-invoice ingestion (Facturae / UBL XML).
- Email ingestion.
- Accounting software specific export formats (A3, Sage, Holded...).
- Dashboards and spending analytics.
- Billing (Stripe) and subscription plans.
- Mobile app.

## Open questions

- ~~Auth: ASP.NET Identity vs Entra External ID~~ → Entra External ID ([ADR 0001](adr/0001-authentication-with-entra-external-id.md)).
- Multi-tenancy strategy: shared database with tenant column + EF Core global query filters (likely) vs database per tenant.
- Business validation with real accounting firms is pending; the project is learning-first for now.
