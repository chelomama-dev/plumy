using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Plumy.Application.Invoices.Extraction;

namespace Plumy.Infrastructure.Messaging;

/// <summary>
/// The contract between the API (which sends) and the worker (which receives): queue name and message format,
/// in one place so the two can't drift apart.
/// </summary>
public static class InvoiceExtractionMessages
{
    public const string QueueName = "invoice-extraction";

    /// <summary>Lets a reader of the queue (or the dead-letter queue) tell what a message is without its body.</summary>
    public const string Subject = "InvoiceExtractionRequested";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ServiceBusMessage ToMessage(InvoiceExtractionRequest request) =>
        new(BinaryData.FromObjectAsJson(request, JsonOptions))
        {
            ContentType = "application/json",
            Subject = Subject
        };

    /// <summary>Null when the body isn't a valid request: the message is broken and retrying won't fix it.</summary>
    public static InvoiceExtractionRequest? FromMessage(ServiceBusReceivedMessage message)
    {
        try
        {
            var request = message.Body.ToObjectFromJson<InvoiceExtractionRequest>(JsonOptions);
            return request is { InvoiceId: var invoiceId, FirmId: var firmId }
                   && invoiceId != Guid.Empty && firmId != Guid.Empty
                ? request
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
