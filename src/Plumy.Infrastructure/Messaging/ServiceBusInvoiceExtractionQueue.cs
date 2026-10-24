using Azure.Messaging.ServiceBus;
using Plumy.Application.Invoices.Extraction;

namespace Plumy.Infrastructure.Messaging;

/// <summary>
/// Sends to the Service Bus queue the worker listens on. In Azure the API signs in with its managed identity
/// (Azure Service Bus Data Sender on the namespace); locally, the emulator.
/// </summary>
internal sealed class ServiceBusInvoiceExtractionQueue(ServiceBusClient client) : IInvoiceExtractionQueue, IAsyncDisposable
{
    // Senders are meant to be long-lived: one per queue for the whole app, like the client
    private readonly ServiceBusSender _sender = client.CreateSender(InvoiceExtractionMessages.QueueName);

    public Task EnqueueAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken = default) =>
        _sender.SendMessageAsync(InvoiceExtractionMessages.ToMessage(request), cancellationToken);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
