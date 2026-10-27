# ADR 0003: Read invoices in a background worker fed by a Service Bus queue

- Status: accepted
- Date: 2026-10-24

## Context

An uploaded invoice has to be read: supplier, NIF, number, date, amounts. The reader (Azure Document
Intelligence, next step) takes seconds per file, can be throttled or down, and costs money per page. Doing it
inside the upload request would make uploads slow, tie their success to a third-party service, and leave nothing
to retry when it fails.

## Decision

- **Asynchronous.** The upload saves the file and the row, then sends `{ invoiceId, firmId }` to the Service Bus
  queue `invoice-extraction`. Ids only: the worker reads everything else from the database.
- **A separate worker** (`Plumy.Worker`, its own image and container app) takes the messages. It is not a hosted
  service in the API, so it scales on the queue's length (KEDA `azure-servicebus` rule, 0 to 2 replicas) instead
  of on HTTP traffic, and a slow read never competes with requests.
- **The invoice records its state**: `Pending → Processing → Extracted | Failed`, with a reason for the user when it
  fails. The list shows it and refreshes while any invoice is in progress.
- **At-least-once delivery, idempotent handler.** A finished invoice (Extracted or Failed) is left as it is, so a
  duplicate message is harmless. `Processing` can be started again, so a worker that crashed mid-way is finished
  by the redelivery.
- **One attempt at a time (added 2026-10-27).** Two *different* messages for the same invoice (the upload's, still
  being retried, and a retry's) were both read in parallel: twice the Document Intelligence pages and model calls.
  Each queueing now gives the invoice a new `ExtractionAttemptId` (a fencing token) that the message carries; the
  worker completes a message whose attempt isn't the invoice's without doing anything. Messages sent before this
  have no attempt and are processed as before.
- **Errors.** A file that can't be read (`InvoiceExtractionException`, missing blob) fails the invoice at once. Any
  other error is transient: the worker doesn't settle the message, its lock expires (`LockDuration` 1 minute) and
  Service Bus delivers it again, which spaces the retries out. Abandoning would redeliver it at once and burn
  every attempt in seconds. On the last delivery (`MaxDeliveryCount` 5, the same number in Bicep, the AppHost and
  the worker's settings) the invoice is marked Failed; after it, Service Bus dead-letters the message. Broken
  messages and unknown invoices go straight to the dead-letter queue.
- **Tenancy.** The worker has no user. Each message runs in its own DI scope, with a tenant context set from the
  message's firm, so the same EF Core tenant filter applies: an id from another firm is simply not found.
- **Queueing after the commit, no outbox.** If sending fails after the row is saved, the upload still succeeds
  (it is logged) and the invoice stays Pending. The list offers a retry for Failed invoices and for those Pending
  for five minutes since they were last queued (`ExtractionQueuedAt`; `POST /api/invoices/{id}/extraction`). The
  API refuses an earlier retry with 409: counting from the upload let a second click queue the invoice again.
- **Security.** The namespace has local auth disabled (no SAS keys). The API is *Data Sender* on the queue only;
  the worker is *Data Owner* on the queue only, because KEDA reads the queue's length through the management API,
  which *Data Receiver* doesn't cover. The worker has its own identity: *Storage Blob Data Reader* on the
  `invoices` container and its own SQL user.
- **Basic tier.** Queues and dead-letter queues, billed per operation. No topics, sessions or duplicate detection,
  which this design doesn't need.
- **Rolled out behind a switch.** This step ships the pipeline with a fake reader. `invoiceExtractionEnabled` is
  false in Azure, so the API sends nothing and uploads stay Pending until the real reader lands; then the switch
  goes on and the stuck invoices are retried from the list.

## Alternatives

- **Transactional outbox** (the message saved in the same transaction, published by a separate process): no lost
  messages, but a table, a publisher and its own monitoring. Not worth it while the retry button covers the gap
  and volumes are tiny. The first candidate if lost messages ever show up in the logs.
- **Event Grid on BlobCreated**: no dual write, but the blob is stored before the row, so the event can arrive for an
  invoice that isn't in the database yet (or never will be, if the save fails).
- **Azure Functions with a Service Bus trigger**: less code for the listener, but a second hosting model and a
  second way to deploy, test and configure the same application code.

## Consequences

Good:

- Uploads stay fast and don't depend on the reader. Failures are visible per invoice and retryable.
- The worker costs nothing while the queue is empty, and scales with the backlog.
- The trace of an upload continues in the worker (the message carries the trace context).

Bad:

- One more image, container app, identity and SQL user to keep.
- A message lost between the commit and the send is only recovered when someone clicks retry.
- If the worker can't even save the failure on its last delivery (say, the database is down), the message is
  dead-lettered and the invoice stays Processing. The dead-letter queue has to be watched (no alert yet).
- Data Owner is broader than receiving needs; it is limited to the one queue. Check after the first deployment
  whether KEDA works with Data Receiver alone, and narrow it if so.
