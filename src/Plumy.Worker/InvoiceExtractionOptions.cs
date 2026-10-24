using System.ComponentModel.DataAnnotations;

namespace Plumy.Worker;

/// <summary>The InvoiceExtraction section, as the worker reads it.</summary>
internal sealed class InvoiceExtractionOptions
{
    public const string Section = "InvoiceExtraction";

    /// <summary>
    /// Must match the queue's MaxDeliveryCount (Bicep, the AppHost): on that delivery the worker gives up and marks
    /// the invoice as failed, instead of leaving it Processing when Service Bus dead-letters the message.
    /// </summary>
    [Range(1, 100)]
    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>Messages processed at once by each replica. Reading an invoice is mostly waiting on the network.</summary>
    [Range(1, 32)]
    public int MaxConcurrentCalls { get; set; } = 4;
}
