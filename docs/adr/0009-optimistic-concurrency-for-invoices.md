# ADR 0009: Optimistic concurrency for invoices, with a rowversion and If-Match

- Status: accepted
- Date: 2026-10-28

## Context

Several people of a firm review invoices, and the worker writes to them too (the read data, the suggested account).
Until now the last save won (ADR 0008 left this open). Two ways to lose work:

- **A screen that is out of date.** Ana opens an invoice, Luis corrects its total and approves it, and then Ana saves
  her form: the whole data goes back, Luis's total is gone, and Ana never saw it. This is the common case: minutes
  between reading and writing.
- **Two saves at the same moment.** Two requests load the same row and both write it. Milliseconds apart, rare, but
  the database is the only place that can tell.

Locking the invoice while someone has it open (pessimistic concurrency) doesn't fit a web app: a closed tab would
keep the lock.

## Decision

- **A `rowversion` column on `Invoices`** (`Invoice.Version`, mapped with `IsRowVersion()`). SQL Server changes it on
  every write, and EF Core adds it to the `WHERE` of each `UPDATE`: when the row changed after it was loaded, no row
  is updated and EF throws `DbUpdateConcurrencyException`. That covers the two saves at the same moment, the worker's
  included, and the API answers **409 Conflict**.
- **The version goes to the client**: in every invoice of the body (`version`, hex like `00000000000007D1`, since
  lists have no ETag) and in the `ETag` header of `GET /api/invoices/{id}` and of the changes' responses.
- **The client sends it back in `If-Match`** on the three changes (`PUT .../data`, `.../ledger-account`,
  `.../review`). The handler compares it with the stored version before doing anything else, and an older one gets
  **412 Precondition Failed**: the screen must load the invoice again. That covers the out-of-date screen.
- **`If-Match` is optional.** Without it (or with `*`), the change goes through as before, still protected against
  two saves at once. A missing header isn't an error (`428 Precondition Required`), so a script or an older client
  keeps working; our front always sends it.
- **Strong comparison**, as RFC 9110 asks of `If-Match`: a weak tag (`W/"..."`) never matches, and a header that
  isn't a tag is compared as it came (so it fails) rather than skipped.
- **A repeated review isn't refused.** `PUT .../review` asking for the state the invoice is already in returns it
  before the version is compared: a retry of a request that worked finds a newer version (its own) and must get the
  same answer, as ADR 0008 promised.
- The domain owns the version as plain data (`byte[]`): the aggregate is what is versioned, the database fills it.

## Alternatives

- **The version in the body of each change** (`{ "version": "...", ... }`) and 409: simpler for a client, but it
  repeats what HTTP already defines; `If-Match`/`ETag`/412 is what proxies, tools and other developers expect.
- **Only the rowversion, no If-Match**: catches two saves at once, not the out-of-date screen, which is the case that
  happens.
- **A version number kept by hand** (`int Version`, incremented in the domain): works on any database, but has to be
  remembered in every change. The rowversion can't be forgotten.
- **Comparing field by field** (only refuse when the same fields changed): fewer refusals, much more code, and a
  merge the person doesn't see.

## Consequences

Good:

- No change made from an out-of-date screen silently overwrites someone else's; the person sees what changed.
- HTTP's own mechanism: the API explains itself to anyone who knows `ETag`.

Bad:

- A screen open while the worker finishes (the account suggestion arrives) gets a 412 on its first change. The list
  already refreshes while an invoice is being read, so this should be rare; the front reloads and says why.
- The worker can now hit a 409-style conflict if a person saves the same invoice at the same moment. The message is
  retried, and the handler skips what is already done (ADR 0003), so it costs at most a repeated suggestion.
